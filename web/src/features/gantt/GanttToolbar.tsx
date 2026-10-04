import type { Gantt } from '../../api/types';
import { Icon } from '../../components/Icon';
import { Button, MultiSelect, Popover } from '../../components/ui';
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
import { applyQuickFilter, clearFilters, DEFAULT_COLUMNS, hasFilters, isQuickFilterOn, type GanttConditions, type QuickFilter } from './model/conditions';
import styles from './gantt.module.css';

const QUICK: Array<{ id: QuickFilter; label: string }> = [
  { id: 'mine', label: '自分のタスク' },
  { id: 'due_this_week', label: '今週が期限' },
  { id: 'delayed', label: '遅れ' },
  { id: 'unassigned', label: '未割り当て' },
];

/**
 * 条件の帯（要件定義書 6.3）。1 段目は「何を表示するか」（すぐ絞る、絞り込み）、
 * 2 段目は「どう表示するか」（表示期間、目盛り、まとめ方、並べ替え、色分け、列、ビュー）。
 */
export function GanttToolbar({
  conditions,
  data,
  teamOptions,
  onChange,
  onToday,
  viewMenu,
  onCreate,
  variant = 'gantt',
}: {
  /** list はタスク一覧の画面。ガントだけの表示の設定（目盛り、まとめ方、色分け、列など）は出さない */
  variant?: 'gantt' | 'list';
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
  const filtered = hasFilters(conditions);
  // 表示の設定のうち、初めの状態から変えている数（ボタンに数を出して、変えていることに気づけるようにする）
  const displayChanges =
    (conditions.sort !== 'manual' ? 1 : 0) +
    (conditions.color !== 'status' ? 1 : 0) +
    (conditions.cols.join(',') !== DEFAULT_COLUMNS.join(',') ? 1 : 0) +
    (conditions.dense ? 1 : 0);

  return (
    <div className={styles.toolbar}>
      <div className={styles.toolbarLine}>
        <div className={styles.toolbarRow}>
          <div className={styles.group} role="group" aria-label="すぐ絞る">
            {QUICK.map((q) => {
              const on = isQuickFilterOn(conditions, q.id);
              return (
                <Button key={q.id} size="small" className={styles.chip} pressed={on} onClick={() => onChange(applyQuickFilter(conditions, q.id, !on))}>
                  {on && <Icon name="check" size={14} />}
                  {q.label}
                </Button>
              );
            })}
            <Button size="small" className={styles.chip} pressed={conditions.milestones} onClick={() => set('milestones', !conditions.milestones)}>
              {conditions.milestones && <Icon name="check" size={14} />}
              マイルストーンだけ
            </Button>
          </div>
          <span className={styles.divider} aria-hidden="true" />
          <div className={styles.group} role="group" aria-label="絞り込み">
            <span className={styles.groupLabel}>
              <Icon name="filter" size={14} />
              絞り込み
            </span>
            <MultiSelect label="チーム" options={teamOptions} selected={conditions.teams} onChange={(v) => set('teams', v)} />
            <MultiSelect label="担当者" options={assigneeOptions} selected={conditions.assignees} onChange={(v) => set('assignees', v)} />
            <MultiSelect label="状態" options={STATUS_ORDER.map((s) => ({ value: s, label: `${STATUS[s].icon} ${STATUS[s].label}` }))} selected={conditions.status} onChange={(v) => set('status', v)} />
            <MultiSelect label="優先度" options={PRIORITY_ORDER.map((p) => ({ value: p, label: PRIORITY[p].label }))} selected={conditions.priority} onChange={(v) => set('priority', v)} />
            <MultiSelect label="タグ" options={(data?.tags ?? []).map((t) => ({ value: t.id, label: t.name }))} selected={conditions.tags} onChange={(v) => set('tags', v)} />
            <MultiSelect label="遅れ・超過" options={FLAG_ORDER.map((f) => ({ value: f, label: FLAG[f].label }))} selected={conditions.flags} onChange={(v) => set('flags', v)} />
          </div>
          {conditions.q.trim() && (
            <span className={styles.keyword}>
              キーワード「{conditions.q}」
              <button type="button" aria-label="キーワードでの絞り込みを外す" onClick={() => set('q', '')}>
                <Icon name="close" size={14} />
              </button>
            </span>
          )}
          {filtered && (
            <Button size="small" variant="ghost" onClick={() => onChange(clearFilters(conditions))}>
              <Icon name="close" size={14} />
              条件を解除
            </Button>
          )}
        </div>
        {onCreate && (
          <Button variant="primary" onClick={onCreate}>
            <Icon name="plus" size={16} />
            タスクを追加
          </Button>
        )}
      </div>

      <div className={[styles.toolbarLine, styles.toolbarSub].join(' ')}>
        <div className={styles.toolbarRow}>
          <div className={styles.group}>
            <label className={styles.groupLabel} htmlFor="gantt-range">
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
          </div>
          {variant === 'gantt' && (
            <>
              <div className={styles.group}>
                <span className={styles.groupLabel}>目盛り</span>
                <span className={styles.segmented} role="group" aria-label="目盛り">
                  {(Object.keys(ZOOM_LABEL) as Zoom[]).map((z) => (
                    <Button key={z} size="small" pressed={conditions.zoom === z} onClick={() => set('zoom', z)}>
                      {ZOOM_LABEL[z]}
                    </Button>
                  ))}
                </span>
                <Button size="small" onClick={onToday}>
                  <Icon name="today" size={14} />
                  今日へ
                </Button>
              </div>
              <span className={styles.divider} aria-hidden="true" />
              <div className={styles.group}>
                <label className={styles.groupLabel} htmlFor="gantt-group">
                  まとめ方
                </label>
                <select id="gantt-group" value={conditions.group} onChange={(e) => set('group', e.target.value as GroupBy)}>
                  {(Object.keys(GROUP_LABEL) as GroupBy[]).map((g) => (
                    <option key={g} value={g}>
                      {GROUP_LABEL[g]}
                    </option>
                  ))}
                </select>
                <Popover label="表示の設定" icon="sliders" badge={displayChanges}>
                  <label className={styles.popField} htmlFor="gantt-sort">
                    並べ替え
                    <select id="gantt-sort" value={conditions.sort} onChange={(e) => set('sort', e.target.value as SortBy)}>
                      {(Object.keys(SORT_LABEL) as SortBy[]).map((x) => (
                        <option key={x} value={x}>
                          {SORT_LABEL[x]}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className={styles.popField} htmlFor="gantt-color">
                    バーの色分け
                    <select id="gantt-color" value={conditions.color} onChange={(e) => set('color', e.target.value as ColorBy)}>
                      {(Object.keys(COLOR_LABEL) as ColorBy[]).map((c) => (
                        <option key={c} value={c}>
                          {COLOR_LABEL[c]}
                        </option>
                      ))}
                    </select>
                  </label>
                  <fieldset className={styles.popColumns}>
                    <legend>左の表に出す列</legend>
                    {ALL_COLUMNS.map((c) => (
                      <label key={c} className="check-label">
                        <input
                          type="checkbox"
                          checked={conditions.cols.includes(c)}
                          onChange={(e) => set('cols', ALL_COLUMNS.filter((x) => (x === c ? e.target.checked : conditions.cols.includes(x))) as Column[])}
                        />
                        {COLUMN_LABEL[c]}
                      </label>
                    ))}
                  </fieldset>
                  <label className="check-label">
                    <input type="checkbox" checked={conditions.dense} onChange={(e) => set('dense', e.target.checked)} />
                    詰めて表示する（1 行を低くして、多くの行を表示）
                  </label>
                </Popover>
              </div>
            </>
          )}
        </div>
        <div className={styles.group}>{viewMenu}</div>
      </div>
    </div>
  );
}
