import type { Gantt } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Button, MultiSelect } from '../../components/ui';
import type { RangePreset } from '../../lib/dates';
import {
  ALL_COLUMNS,
  COLOR_LABEL,
  COLUMN_LABEL,
  FLAG,
  FLAG_ORDER,
  GROUP_LABEL,
  PRIORITY,
  PRIORITY_ORDER,
  RANGE_LABEL,
  SORT_LABEL,
  STATUS,
  STATUS_ORDER,
  ZOOM_LABEL,
  type ColorBy,
  type Column,
  type GroupBy,
  type SortBy,
  type Zoom,
} from '../../lib/labels';
import { applyQuickFilter, isQuickFilterOn, type GanttConditions, type QuickFilter } from './model/conditions';
import styles from './gantt.module.css';

const QUICK: Array<{ id: QuickFilter; label: string }> = [
  { id: 'mine', label: '自分のタスク' },
  { id: 'due_this_week', label: '今週が期限' },
  { id: 'delayed', label: '遅れ' },
  { id: 'unassigned', label: '未割り当て' },
];

/** 条件の帯（要件定義書 6.3）。すぐ絞る、絞り込み、表示期間、目盛り、まとめ方、並べ替え、色分け、列。 */
export function GanttToolbar({
  conditions,
  data,
  teamOptions,
  onChange,
  onToday,
  viewMenu,
  onCreate,
}: {
  conditions: GanttConditions;
  data: Gantt | undefined;
  teamOptions: Array<{ value: string; label: string }>;
  onChange: (next: GanttConditions) => void;
  onToday: () => void;
  viewMenu: React.ReactNode;
  onCreate?: () => void;
}) {
  const set = <K extends keyof GanttConditions>(key: K, value: GanttConditions[K]) => onChange({ ...conditions, [key]: value });
  const assigneeOptions = [
    { value: 'me', label: '自分' },
    { value: 'unassigned', label: '未割り当て' },
    ...(data?.members ?? []).filter((m) => m.current).map((m) => ({ value: m.id, label: m.displayName })),
  ];

  return (
    <div className={styles.toolbar}>
      <div className={styles.toolbarRow}>
        <span className={styles.toolbarLabel}>すぐ絞る</span>
        {QUICK.map((q) => {
          const on = isQuickFilterOn(conditions, q.id);
          return (
            <Button key={q.id} size="small" pressed={on} onClick={() => onChange(applyQuickFilter(conditions, q.id, !on))}>
              {q.label}
            </Button>
          );
        })}
        <span className={styles.toolbarLabel}>絞り込み</span>
        <MultiSelect label="チーム" options={teamOptions} selected={conditions.teams} onChange={(v) => set('teams', v)} />
        <MultiSelect label="担当者" options={assigneeOptions} selected={conditions.assignees} onChange={(v) => set('assignees', v)} />
        <MultiSelect label="状態" options={STATUS_ORDER.map((s) => ({ value: s, label: `${STATUS[s].icon} ${STATUS[s].label}` }))} selected={conditions.status} onChange={(v) => set('status', v)} />
        <MultiSelect label="優先度" options={PRIORITY_ORDER.map((p) => ({ value: p, label: PRIORITY[p].label }))} selected={conditions.priority} onChange={(v) => set('priority', v)} />
        <MultiSelect label="タグ" options={(data?.tags ?? []).map((t) => ({ value: t.id, label: t.name }))} selected={conditions.tags} onChange={(v) => set('tags', v)} />
        <MultiSelect label="遅れ・超過" options={FLAG_ORDER.map((f) => ({ value: f, label: FLAG[f].label }))} selected={conditions.flags} onChange={(v) => set('flags', v)} />
        <label className="row">
          <input type="checkbox" checked={conditions.milestones} onChange={(e) => set('milestones', e.target.checked)} />
          マイルストーンだけ
        </label>
        {onCreate && (
          <Button variant="primary" size="small" onClick={onCreate}>
            <Icon name="plus" size={14} />
            タスクを追加
          </Button>
        )}
      </div>
      <div className={styles.toolbarRow}>
        <label className={styles.toolbarLabel} htmlFor="gantt-range">
          表示期間
        </label>
        <select id="gantt-range" value={conditions.range} onChange={(e) => set('range', e.target.value as RangePreset)}>
          {(Object.keys(RANGE_LABEL) as RangePreset[]).map((r) => (
            <option key={r} value={r}>
              {RANGE_LABEL[r]}
            </option>
          ))}
        </select>
        {conditions.range === 'custom' && (
          <>
            <input type="date" aria-label="表示期間の開始" value={conditions.from ?? ''} onChange={(e) => onChange({ ...conditions, from: e.target.value || null })} />
            〜
            <input type="date" aria-label="表示期間の終了" value={conditions.to ?? ''} min={conditions.from ?? undefined} onChange={(e) => onChange({ ...conditions, to: e.target.value || null })} />
          </>
        )}
        <span className={styles.toolbarLabel}>目盛り</span>
        <span className={styles.segmented} role="group" aria-label="目盛り">
          {(Object.keys(ZOOM_LABEL) as Zoom[]).map((z) => (
            <Button key={z} size="small" pressed={conditions.zoom === z} onClick={() => set('zoom', z)}>
              {ZOOM_LABEL[z]}
            </Button>
          ))}
        </span>
        <Button size="small" onClick={onToday}>
          今日へ
        </Button>
        <label className={styles.toolbarLabel} htmlFor="gantt-group">
          まとめ方
        </label>
        <select id="gantt-group" value={conditions.group} onChange={(e) => set('group', e.target.value as GroupBy)}>
          {(Object.keys(GROUP_LABEL) as GroupBy[]).map((g) => (
            <option key={g} value={g}>
              {GROUP_LABEL[g]}
            </option>
          ))}
        </select>
        <label className={styles.toolbarLabel} htmlFor="gantt-sort">
          並べ替え
        </label>
        <select id="gantt-sort" value={conditions.sort} onChange={(e) => set('sort', e.target.value as SortBy)}>
          {(Object.keys(SORT_LABEL) as SortBy[]).map((s) => (
            <option key={s} value={s}>
              {SORT_LABEL[s]}
            </option>
          ))}
        </select>
        <label className={styles.toolbarLabel} htmlFor="gantt-color">
          色分け
        </label>
        <select id="gantt-color" value={conditions.color} onChange={(e) => set('color', e.target.value as ColorBy)}>
          {(Object.keys(COLOR_LABEL) as ColorBy[]).map((c) => (
            <option key={c} value={c}>
              {COLOR_LABEL[c]}
            </option>
          ))}
        </select>
        <MultiSelect label="列" options={ALL_COLUMNS.map((c) => ({ value: c, label: COLUMN_LABEL[c] }))} selected={conditions.cols} onChange={(v: Column[]) => set('cols', ALL_COLUMNS.filter((c) => v.includes(c)))} />
        <label className="row">
          <input type="checkbox" checked={conditions.dense} onChange={(e) => set('dense', e.target.checked)} />
          詰めて表示
        </label>
        {viewMenu}
      </div>
    </div>
  );
}
