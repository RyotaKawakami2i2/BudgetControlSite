import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api } from '../../api/client';
import { invalidateTaskData, keys, useGantt, useGanttSearch, useMe, useViews } from '../../api/hooks';
import type { Gantt, GanttTask } from '../../api/types';
import { ErrorBox, Loading, describeError, useToast } from '../../components/ui';
import { readCurrentTeamId, useSyncCurrentTeam, useTaskPanel } from '../../layout/context';
import { presetRange, toDay, todayIso } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { COLOR_LABEL, PRIORITY, PRIORITY_ORDER, ROLE, STATUS, STATUS_ORDER, tagColorVar, SERIES_COLORS } from '../../lib/labels';
import { TaskFormDialog, type TaskFormTarget } from '../tasks/TaskFormDialog';
import { GanttChart } from './GanttChart';
import { GanttToolbar } from './GanttToolbar';
import { fromSearchParams, fromViewConditions, toSearchParams, type GanttConditions } from './model/conditions';
import { buildRows } from './model/rows';
import { ViewMenu } from './ViewMenu';
import styles from './gantt.module.css';

const CONDITION_KEYS = ['teams', 'range', 'group', 'sort', 'assignees', 'status', 'priority', 'tags', 'flags', 'milestones', 'due', 'q', 'zoom', 'cols', 'color', 'dense'];

/** ガントチャート（SC-05。要件定義書 6.3）。 */
export function GanttPage() {
  const [params, setParams] = useSearchParams();
  const { data: me } = useMe();
  const { data: views } = useViews();
  const panel = useTaskPanel();
  const toast = useToast();
  const queryClient = useQueryClient();
  const conditions = useMemo(() => fromSearchParams(params), [params]);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [selectedId, setSelectedId] = useState<string | null>(panel.taskId);
  const [todaySignal, setTodaySignal] = useState(0);
  const [form, setForm] = useState<TaskFormTarget | null>(null);
  const undo = useRef<Array<{ id: string; start: string | null; end: string | null }>>([]);
  const appliedDefault = useRef(false);
  const narrow = useNarrow();

  const update = useCallback(
    (next: GanttConditions, replace = true) => {
      const task = params.get('task');
      setParams(toSearchParams(next, { task }), { replace });
    },
    [params, setParams],
  );

  // 最初に開くビュー（条件の指定がない URL で開いたとき。FR-GNT-26）
  useEffect(() => {
    if (appliedDefault.current || !me || !views) return;
    appliedDefault.current = true;
    if (CONDITION_KEYS.some((k) => params.has(k)) || !me.defaultViewId) return;
    const view = views.find((v) => v.id === me.defaultViewId);
    if (view) update(fromViewConditions(view.conditions));
  }, [me, views, params, update]);

  useSyncCurrentTeam(conditions.teams.length === 1 ? conditions.teams[0] : null);
  const currentTeam = readCurrentTeamId(me);
  const teamIds = useMemo(
    () => (conditions.teams.length > 0 ? conditions.teams : currentTeam ? [currentTeam] : []),
    [conditions.teams, currentTeam],
  );
  const today = todayIso();
  const range = presetRange(conditions.range, today, { from: conditions.from ?? undefined, to: conditions.to ?? undefined });
  const gantt = useGantt(teamIds, range.from, range.to);
  const search = useGanttSearch(teamIds, conditions.q);
  const data = gantt.data;

  const result = useMemo(() => {
    if (!data || !me) return null;
    return buildRows(
      {
        tasks: data.tasks,
        teams: data.teams,
        members: data.members,
        meId: me.id,
        today: data.asOf,
        range,
        holidays: new Set(data.holidays.map((h) => toDay(h.date))),
        searchIds: conditions.q.trim() ? new Set(search.data?.taskIds ?? []) : null,
      },
      conditions,
      collapsed,
    );
    // range は conditions から決まる
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, me, conditions, collapsed, search.data]);

  const readOnly = narrow || (data?.teams.every((t) => t.role === 'admin_view' || t.archived) ?? true);

  const toggle = useCallback((key: string, collapse?: boolean) => {
    setCollapsed((prev) => {
      const next = new Set(prev);
      const shouldCollapse = collapse ?? !next.has(key);
      if (shouldCollapse) next.add(key);
      else next.delete(key);
      return next;
    });
  }, []);

  const open = useCallback(
    (id: string) => {
      setSelectedId(id);
      panel.open(id);
    },
    [panel],
  );

  /** ドラッグで日程を変える（FR-GNT-14）。変更はすぐに保存し、失敗したら元に戻す。409 なら最新を読み込む。 */
  const reschedule = useCallback(
    async (task: GanttTask, start: string, end: string, isUndo = false) => {
      const key = keys.gantt(teamIds, range.from, range.to);
      queryClient.setQueryData<Gantt>(key, (old) =>
        old ? { ...old, tasks: old.tasks.map((t) => (t.id === task.id ? { ...t, plannedStart: start, plannedEnd: end } : t)) } : old,
      );
      try {
        await api.patch(`/tasks/${task.id}`, { version: task.version, plannedStart: start, plannedEnd: end });
        if (!isUndo) {
          undo.current.push({ id: task.id, start: task.plannedStart, end: task.plannedEnd });
          if (undo.current.length > 20) undo.current.shift();
        }
        toast.show('success', isUndo ? '日程の変更を取り消しました。' : '日程を変更しました（Ctrl + Z で取り消せます）。');
      } catch (error) {
        toast.show('error', describeError(error));
      } finally {
        invalidateTaskData(queryClient, task.id);
      }
    },
    [queryClient, teamIds, range.from, range.to, toast],
  );

  // 直前の日程の変更を取り消す（FR-GNT-15）
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== 'z' || target?.closest('input, textarea, select, dialog')) return;
      const last = undo.current.pop();
      const task = last && data?.tasks.find((t) => t.id === last.id);
      if (!last || !task || !last.start || !last.end) return;
      e.preventDefault();
      void reschedule(task, last.start, last.end, true);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [data, reschedule]);

  if (!me) return <Loading />;
  if (teamIds.length === 0) {
    return (
      <div className="page">
        <h1>ガント</h1>
        <p className="empty">所属しているチームがありません。チームのリーダーか管理者に、チームへの追加を依頼してください。</p>
      </div>
    );
  }

  const teamOptions = me.teams.map((t) => ({ value: t.id, label: t.name }));
  const adminView = data?.teams.some((t) => t.role === 'admin_view');
  const writableTeam = data?.teams.find((t) => t.role !== 'admin_view' && !t.archived);

  return (
    <div className={styles.page}>
      <h1 className="visually-hidden">ガントチャート</h1>
      <GanttToolbar
        conditions={conditions}
        data={data}
        teamOptions={teamOptions}
        onChange={(next) => update(next)}
        onToday={() => setTodaySignal((n) => n + 1)}
        onCreate={!readOnly && writableTeam ? () => setForm({ mode: 'create', teamId: writableTeam.id }) : undefined}
        viewMenu={<ViewMenu conditions={conditions} onApply={(c) => update(c, false)} />}
      />
      {adminView && <p className={styles.notice}>{ROLE.admin_view}: 所属していないチームは、閲覧だけができます（閲覧したことは監査ログに残ります）。</p>}
      {data?.teams.some((t) => t.archived) && <p className={styles.notice}>アーカイブしたチームは読み取り専用です。</p>}
      {narrow && <p className={styles.notice}>スマートフォンでは、ガントは閲覧だけができます。</p>}
      {data?.truncated && <p className={[styles.notice, styles.noticeWarning].join(' ')}>タスクが多いため、一部だけを表示しています。条件を絞ってください。</p>}
      {gantt.error && <ErrorBox error={gantt.error} />}
      {gantt.isLoading && <Loading />}
      {data && result && (
        <>
          {result.rows.length === 0 ? (
            <p className={styles.empty}>条件に合うタスクはありません。</p>
          ) : (
            <GanttChart
              rows={result.rows}
              data={data}
              range={range}
              conditions={conditions}
              selectedId={selectedId}
              readOnly={readOnly}
              scrollToTodaySignal={todaySignal}
              onSelect={setSelectedId}
              onOpen={open}
              onToggle={toggle}
              onReschedule={(task, start, end) => void reschedule(task, start, end)}
            />
          )}
          <div className={styles.footer} aria-live="polite">
            <span>表示 {result.totals.count}件</span>
            <span>予定工数 {formatHours(result.totals.plannedMinutes)}</span>
            <span>実績工数 {formatHours(result.totals.actualMinutes)}</span>
            <span>差 {formatSignedHours(result.totals.actualMinutes - result.totals.plannedMinutes)}</span>
            <Legend conditions={conditions} data={data} />
          </div>
        </>
      )}
      {form && <TaskFormDialog target={form} onClose={() => setForm(null)} onSaved={(t) => open(t.id)} />}
    </div>
  );
}

/** 凡例（FR-GNT-09）。 */
function Legend({ conditions, data }: { conditions: GanttConditions; data: Gantt }) {
  let items: Array<{ label: string; color: string }>;
  switch (conditions.color) {
    case 'priority':
      items = PRIORITY_ORDER.map((p) => ({ label: `優先度 ${PRIORITY[p].label}`, color: PRIORITY[p].color }));
      break;
    case 'assignee':
      items = data.members.filter((m) => m.current).map((m, i) => ({ label: m.displayName, color: SERIES_COLORS[i % SERIES_COLORS.length]! }));
      break;
    case 'tag':
      items = data.tags.map((t) => ({ label: t.name, color: tagColorVar(t.color).fg }));
      break;
    default:
      items = STATUS_ORDER.map((s) => ({ label: `${STATUS[s].icon} ${STATUS[s].label}`, color: STATUS[s].color }));
  }
  return (
    <span className={styles.legend} aria-label={`凡例（${COLOR_LABEL[conditions.color]}）`}>
      {items.map((i) => (
        <span key={i.label} className={styles.legendItem}>
          <span className={styles.legendSwatch} style={{ background: i.color }} />
          {i.label}
        </span>
      ))}
      <span className={styles.legendItem}>◆ マイルストーン</span>
      <span className={styles.legendItem}>━ 実績</span>
      <span className={styles.legendItem}>！ 遅れ・超過</span>
    </span>
  );
}

function useNarrow(): boolean {
  const query = '(max-width: 768px)';
  const [narrow, setNarrow] = useState(() => window.matchMedia(query).matches);
  useEffect(() => {
    const media = window.matchMedia(query);
    const onChange = () => setNarrow(media.matches);
    media.addEventListener('change', onChange);
    return () => media.removeEventListener('change', onChange);
  }, []);
  return narrow;
}
