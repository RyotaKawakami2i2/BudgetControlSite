import { useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import { useGantt, useGanttSearch, useMe } from '../../api/hooks';
import type { GanttTask } from '../../api/types';
import { Button, ErrorBox, FlagBadges, Loading, StatusBadge } from '../../components/ui';
import { readCurrentTeamId, useTaskPanel } from '../../layout/context';
import { formatDate, presetRange, toDay, todayIso } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { PRIORITY } from '../../lib/labels';
import { GanttToolbar } from '../gantt/GanttToolbar';
import { fromSearchParams, toSearchParams } from '../gantt/model/conditions';
import { buildRows, type TaskRow } from '../gantt/model/rows';
import { ViewMenu } from '../gantt/ViewMenu';

type SortKey = 'title' | 'assignee' | 'status' | 'priority' | 'plannedStart' | 'plannedEnd' | 'progress' | 'plannedMinutes' | 'actualMinutes' | 'variance';

/** タスク一覧（SC-10。FR-LST-02）。ガントと同じ条件で絞り込んだ結果を表で表示し、列ごとに並べ替えられる。 */
export function TaskListPage() {
  const [params, setParams] = useSearchParams();
  const { data: me } = useMe();
  const { open } = useTaskPanel();
  const conditions = useMemo(() => fromSearchParams(params), [params]);
  const teamIds = conditions.teams.length > 0 ? conditions.teams : [readCurrentTeamId(me)].filter((v): v is string => !!v);
  const range = presetRange(conditions.range, todayIso(), { from: conditions.from ?? undefined, to: conditions.to ?? undefined });
  const gantt = useGantt(teamIds, range.from, range.to);
  const search = useGanttSearch(teamIds, conditions.q);
  const [sort, setSort] = useState<{ key: SortKey; desc: boolean }>({ key: 'plannedEnd', desc: false });

  const data = gantt.data;
  const memberName = useMemo(() => new Map((data?.members ?? []).map((m) => [m.id, m.displayName])), [data]);
  const tasks = useMemo(() => {
    if (!data || !me) return [];
    const { rows } = buildRows(
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
      { ...conditions, group: 'hierarchy' },
      new Set(),
    );
    const list = rows.filter((r): r is TaskRow => r.kind === 'task' && !r.context).map((r) => r.task);
    const value = (t: GanttTask): string | number => {
      switch (sort.key) {
        case 'title':
          return t.title;
        case 'assignee':
          return t.assigneeId ? memberName.get(t.assigneeId) ?? '' : '';
        case 'status':
          return t.status;
        case 'priority':
          return PRIORITY[t.priority].rank;
        case 'plannedStart':
          return t.plannedStart ?? '9999';
        case 'plannedEnd':
          return t.plannedEnd ?? '9999';
        case 'progress':
          return t.progress;
        case 'plannedMinutes':
          return t.plannedMinutes ?? -1;
        case 'actualMinutes':
          return t.actualMinutes;
        case 'variance':
          return t.plannedMinutes === null ? -Infinity : t.actualMinutes - t.plannedMinutes;
      }
    };
    return [...list].sort((a, b) => {
      const va = value(a);
      const vb = value(b);
      const cmp = typeof va === 'number' && typeof vb === 'number' ? va - vb : String(va).localeCompare(String(vb), 'ja');
      return sort.desc ? -cmp : cmp;
    });
    // range は conditions から決まる
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, me, conditions, search.data, sort, memberName]);

  const header = (key: SortKey, label: string, num = false) => (
    <th scope="col" className={num ? 'num' : undefined} aria-sort={sort.key === key ? (sort.desc ? 'descending' : 'ascending') : 'none'}>
      <Button variant="ghost" size="small" onClick={() => setSort({ key, desc: sort.key === key ? !sort.desc : false })}>
        {label}
        {sort.key === key ? (sort.desc ? ' ▼' : ' ▲') : ''}
      </Button>
    </th>
  );

  return (
    <div>
      <GanttToolbar
        conditions={conditions}
        data={data}
        teamOptions={(me?.teams ?? []).map((t) => ({ value: t.id, label: t.name }))}
        onChange={(next) => setParams(toSearchParams(next, { task: params.get('task') }), { replace: true })}
        onToday={() => undefined}
        viewMenu={<ViewMenu conditions={conditions} onApply={(c) => setParams(toSearchParams(c))} />}
      />
      <div className="page">
        <div className="page-header">
          <h1>タスク一覧</h1>
          <span className="muted">{tasks.length}件</span>
        </div>
        {gantt.isLoading && <Loading />}
        {gantt.error && <ErrorBox error={gantt.error} />}
        {data && (
          <div className="scroll-x">
            <table className="data-table">
              <thead>
                <tr>
                  {header('title', 'タスク')}
                  {header('assignee', '担当')}
                  {header('status', '状態')}
                  {header('priority', '優先度')}
                  {header('plannedStart', '予定開始')}
                  {header('plannedEnd', '予定終了')}
                  {header('progress', '進捗', true)}
                  {header('plannedMinutes', '予定工数', true)}
                  {header('actualMinutes', '実績工数', true)}
                  {header('variance', '工数差', true)}
                </tr>
              </thead>
              <tbody>
                {tasks.map((t) => (
                  <tr key={t.id}>
                    <td>
                      <Button variant="link" onClick={() => open(t.id)}>
                        {t.isMilestone && '◆ '}
                        {t.title}
                      </Button>
                      <div className="row">
                        <FlagBadges flags={t.flags} short />
                      </div>
                    </td>
                    <td>{t.assigneeId ? memberName.get(t.assigneeId) : t.isSummary ? '' : '未割り当て'}</td>
                    <td>
                      <StatusBadge status={t.status} />
                    </td>
                    <td>{PRIORITY[t.priority].label}</td>
                    <td className="nowrap">{formatDate(t.plannedStart)}</td>
                    <td className="nowrap">{formatDate(t.plannedEnd)}</td>
                    <td className="num">{t.progress}%</td>
                    <td className="num">{formatHours(t.plannedMinutes, '-')}</td>
                    <td className="num">{formatHours(t.actualMinutes)}</td>
                    <td className="num">{t.plannedMinutes === null ? '-' : formatSignedHours(t.actualMinutes - t.plannedMinutes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
