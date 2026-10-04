import { useVirtualizer } from '@tanstack/react-virtual';
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent as ReactPointerEvent } from 'react';
import type { Gantt, GanttTask } from '../../api/types';
import { Icon } from '../../components/Icon';
import { addDays, formatShortDate, isWeekend, toDay } from '../../lib/dates';
import { formatHours, formatSignedHours } from '../../lib/effort';
import { COLUMN_LABEL, FLAG, PRIORITY, SERIES_COLORS, STATUS, tagColorVar, type Column } from '../../lib/labels';
import type { GanttConditions } from './model/conditions';
import type { GanttRow, GroupRow, TaskRow } from './model/rows';
import { DAY_WIDTH, ROW, ticks, widthOf, xOf } from './model/scale';
import styles from './gantt.module.css';

const HEADER_HEIGHT = 44;
const NAME_WIDTH = 280;
const COLUMN_WIDTH: Record<Column, number> = {
  assignee: 96,
  status: 84,
  progress: 64,
  planned_dates: 140,
  actual_dates: 140,
  planned_minutes: 72,
  actual_minutes: 72,
  variance: 72,
  priority: 56,
  tags: 120,
};
const EDGE = 6;

interface DragState {
  taskId: string;
  mode: 'move' | 'start' | 'end';
  originX: number;
  start: string;
  end: string;
  delta: number;
}

export interface GanttChartProps {
  rows: GanttRow[];
  data: Gantt;
  range: { from: string; to: string };
  conditions: GanttConditions;
  selectedId: string | null;
  readOnly: boolean;
  scrollToTodaySignal: number;
  onSelect: (taskId: string) => void;
  onOpen: (taskId: string) => void;
  onToggle: (key: string, collapse?: boolean) => void;
  onReschedule: (task: GanttTask, start: string, end: string) => void;
}

/**
 * ガントチャート（詳細設計書 7.3）。左に階層付きのタスク一覧、右に時間軸。見えている行と前後 10 行だけを描く。
 * 部品は画面の他の部分から切り離し、入力は「行の一覧と表示条件」、出力は「操作の通知」とする（基本設計書 2.6）。
 */
export function GanttChart(props: GanttChartProps) {
  const { rows, data, range, conditions, selectedId, readOnly } = props;
  const rightRef = useRef<HTMLDivElement>(null);
  const leftRef = useRef<HTMLDivElement>(null);
  const size = conditions.dense ? ROW.dense : ROW.normal;
  const dw = DAY_WIDTH[conditions.zoom];
  const days = toDay(range.to) - toDay(range.from) + 1;
  const width = Math.max(days * dw, 200);
  const totalHeight = rows.length * size.height;
  const today = data.asOf;
  const holidays = useMemo(() => new Map(data.holidays.map((h) => [toDay(h.date), h.name])), [data.holidays]);
  const memberIndex = useMemo(() => new Map(data.members.map((m, i) => [m.id, i])), [data.members]);
  const memberName = useMemo(() => new Map(data.members.map((m) => [m.id, m.displayName])), [data.members]);
  const tagById = useMemo(() => new Map(data.tags.map((t) => [t.id, t])), [data.tags]);
  const [drag, setDrag] = useState<DragState | null>(null);
  const [tooltip, setTooltip] = useState<{ task: GanttTask; x: number; y: number } | null>(null);

  // 見えている行だけを描く（NF-PRF-02）。TanStack Virtual は React Compiler の自動の最適化の対象外になるが、動作に影響はない
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => rightRef.current,
    estimateSize: () => size.height,
    overscan: 10,
    scrollMargin: HEADER_HEIGHT,
  });

  useEffect(() => {
    virtualizer.measure();
  }, [size.height, virtualizer]);

  // 「今日へ」: 今日の位置が見えるように横に動かす
  useEffect(() => {
    const right = rightRef.current;
    if (!right) return;
    right.scrollLeft = Math.max(0, xOf(today, range.from, dw) - right.clientWidth / 3);
  }, [props.scrollToTodaySignal, today, range.from, dw]);

  const syncScroll = () => {
    if (leftRef.current && rightRef.current) leftRef.current.scrollTop = rightRef.current.scrollTop;
  };

  const color = useCallback(
    (task: GanttTask): string => {
      switch (conditions.color) {
        case 'priority':
          return PRIORITY[task.priority].color;
        case 'assignee':
          return task.assigneeId ? SERIES_COLORS[(memberIndex.get(task.assigneeId) ?? 0) % SERIES_COLORS.length]! : 'var(--color-text-muted)';
        case 'tag': {
          const tag = task.tagIds.map((id) => tagById.get(id)).find(Boolean);
          return tag ? tagColorVar(tag.color).fg : 'var(--color-text-muted)';
        }
        default:
          return STATUS[task.status].color;
      }
    },
    [conditions.color, memberIndex, tagById],
  );

  const rowIndex = useMemo(() => {
    const map = new Map<string, number>();
    rows.forEach((r, i) => r.kind === 'task' && map.set(r.task.id, i));
    return map;
  }, [rows]);

  const preview = (task: GanttTask): { start: string | null; end: string | null } => {
    if (!drag || drag.taskId !== task.id) return { start: task.plannedStart, end: task.plannedEnd };
    const d = drag.delta;
    if (drag.mode === 'move') return { start: addDays(drag.start, d), end: addDays(drag.end, d) };
    if (drag.mode === 'start') {
      const s = addDays(drag.start, d);
      return { start: s > drag.end ? drag.end : s, end: drag.end };
    }
    const e = addDays(drag.end, d);
    return { start: drag.start, end: e < drag.start ? drag.start : e };
  };

  const canDrag = (task: GanttTask) => !readOnly && !task.isSummary && task.can.editPlan && !!task.plannedStart && !!task.plannedEnd;

  const onBarPointerDown = (event: ReactPointerEvent<SVGGElement>, task: GanttTask) => {
    if (event.button !== 0) return;
    props.onSelect(task.id);
    if (!canDrag(task)) return;
    const svg = event.currentTarget.ownerSVGElement;
    if (!svg) return;
    const localX = event.clientX - svg.getBoundingClientRect().left;
    const x = xOf(task.plannedStart!, range.from, dw);
    const w = widthOf(task.plannedStart!, task.plannedEnd!, dw);
    const mode: DragState['mode'] = task.isMilestone ? 'move' : localX - x < EDGE ? 'start' : x + w - localX < EDGE ? 'end' : 'move';
    event.currentTarget.setPointerCapture(event.pointerId);
    setTooltip(null);
    setDrag({ taskId: task.id, mode, originX: event.clientX, start: task.plannedStart!, end: task.plannedEnd!, delta: 0 });
  };

  const onBarPointerMove = (event: ReactPointerEvent<SVGGElement>, task: GanttTask) => {
    if (drag && drag.taskId === task.id) {
      const delta = Math.round((event.clientX - drag.originX) / dw);
      if (delta !== drag.delta) setDrag({ ...drag, delta });
    } else if (!drag) {
      setTooltip({ task, x: event.clientX, y: event.clientY });
    }
  };

  const onBarPointerUp = (task: GanttTask) => {
    if (drag && drag.taskId === task.id) {
      const next = preview(task);
      setDrag(null);
      if (drag.delta !== 0 && next.start && next.end) {
        props.onReschedule(task, next.start, next.end);
        return;
      }
    }
    props.onOpen(task.id);
  };

  // キーボード: ↑↓ で行の移動、→← で開閉、Enter で詳細、Shift + ←→ で日程を 1 日ずらす（詳細設計書 7.3.5）
  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const taskRows = rows.filter((r): r is TaskRow => r.kind === 'task');
    const index = taskRows.findIndex((r) => r.task.id === selectedId);
    const current = index >= 0 ? taskRows[index] : undefined;
    const select = (i: number) => {
      const row = taskRows[Math.max(0, Math.min(taskRows.length - 1, i))];
      if (!row) return;
      props.onSelect(row.task.id);
      const at = rowIndex.get(row.task.id);
      if (at !== undefined) virtualizer.scrollToIndex(at, { align: 'auto' });
    };
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        select(index + 1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        select(index - 1);
        break;
      case 'ArrowRight':
      case 'ArrowLeft': {
        if (!current) break;
        event.preventDefault();
        const forward = event.key === 'ArrowRight';
        if (event.shiftKey) {
          if (canDrag(current.task)) {
            const d = forward ? 1 : -1;
            props.onReschedule(current.task, addDays(current.task.plannedStart!, d), addDays(current.task.plannedEnd!, d));
          }
        } else if (current.hasChildren) {
          props.onToggle(current.key, !forward);
        }
        break;
      }
      case 'Enter':
        if (current) {
          event.preventDefault();
          props.onOpen(current.task.id);
        }
        break;
    }
  };

  const header = useMemo(() => ticks(range.from, range.to, conditions.zoom), [range.from, range.to, conditions.zoom]);
  const leftWidth = NAME_WIDTH + conditions.cols.reduce((sum, c) => sum + COLUMN_WIDTH[c], 0);
  const items = virtualizer.getVirtualItems();
  const todayX = xOf(today, range.from, dw);
  const todayVisible = today >= range.from && today <= range.to;

  return (
    <div className={styles.chart}>
      <div className={styles.left} style={{ width: leftWidth }}>
        <div className={styles.leftHeader} style={{ height: HEADER_HEIGHT }} role="row">
          <span className={[styles.cell, styles.nameCell].join(' ')} role="columnheader">
            タスク名
          </span>
          {conditions.cols.map((c) => (
            <span key={c} className={styles.cell} style={{ width: COLUMN_WIDTH[c] }} role="columnheader">
              {COLUMN_LABEL[c]}
            </span>
          ))}
        </div>
        <div
          ref={leftRef}
          className={styles.leftBody}
          tabIndex={0}
          role="treegrid"
          aria-label="タスクの一覧（↑↓で移動、→←で開閉、Enterで詳細、Shift＋←→で日程を1日ずらす）"
          aria-activedescendant={selectedId ? `gantt-row-${selectedId}` : undefined}
          onKeyDown={onKeyDown}
          onWheel={(e) => {
            if (rightRef.current) rightRef.current.scrollTop += e.deltaY;
          }}
        >
          <div style={{ height: totalHeight, position: 'relative' }}>
            {items.map((item) => {
              const row = rows[item.index]!;
              const top = item.start - HEADER_HEIGHT;
              return row.kind === 'group' ? (
                <GroupLabel key={row.key} row={row} top={top} height={size.height} onToggle={props.onToggle} />
              ) : (
                <TaskLabel
                  key={row.key}
                  row={row}
                  top={top}
                  height={size.height}
                  selected={row.task.id === selectedId}
                  cols={conditions.cols}
                  memberName={memberName}
                  tagById={tagById}
                  onToggle={props.onToggle}
                  onOpen={props.onOpen}
                  onSelect={props.onSelect}
                />
              );
            })}
          </div>
        </div>
      </div>

      <div ref={rightRef} className={styles.right} onScroll={syncScroll}>
        <div className={styles.timeHeader} style={{ width, height: HEADER_HEIGHT }}>
          <svg width={width} height={HEADER_HEIGHT} aria-hidden="true">
            {header.upper.map((t) => (
              <g key={`u${t.x}`}>
                <line x1={t.x} x2={t.x} y1={0} y2={22} style={{ stroke: 'var(--color-border)' }} />
                <text x={t.x + 4} y={15} style={{ fontSize: 12, fill: 'var(--color-text)' }}>
                  {t.width > 30 ? t.label : ''}
                </text>
              </g>
            ))}
            {header.lower.map((t) => (
              <g key={`l${t.x}`}>
                <line x1={t.x} x2={t.x} y1={22} y2={HEADER_HEIGHT} style={{ stroke: 'var(--color-border)' }} />
                <text x={t.x + Math.min(4, t.width / 4)} y={37} style={{ fontSize: 11, fill: 'var(--color-text-muted)' }}>
                  {t.width >= 14 ? t.label : ''}
                </text>
              </g>
            ))}
            {todayVisible && <rect x={todayX} y={22} width={Math.max(dw, 2)} height={HEADER_HEIGHT - 22} style={{ fill: 'var(--color-today)', opacity: 0.25 }} />}
          </svg>
        </div>

        <div className={styles.timeBody} style={{ width, height: Math.max(totalHeight, 1) }}>
          <Background from={range.from} days={days} dw={dw} height={Math.max(totalHeight, 600)} holidays={holidays} todayX={todayVisible ? todayX : null} />
          <DependencyArrows rows={rows} rowIndex={rowIndex} from={range.from} dw={dw} size={size} width={width} height={totalHeight} />
          {items.map((item) => {
            const row = rows[item.index]!;
            const top = item.start - HEADER_HEIGHT;
            if (row.kind === 'group') {
              return row.bar ? (
                <div key={row.key} className={styles.barRow} style={{ top, height: size.height, width }}>
                  <svg width={width} height={size.height} aria-hidden="true">
                    <SummaryBar start={row.bar.start} end={row.bar.end} progress={row.bar.progress} from={range.from} dw={dw} size={size} color="var(--color-text)" />
                  </svg>
                </div>
              ) : null;
            }
            const task = row.task;
            const p = preview(task);
            const selected = task.id === selectedId;
            return (
              <div key={row.key} className={styles.barRow} style={{ top, height: size.height, width, opacity: row.context ? 0.45 : 1 }}>
                <svg width={width} height={size.height}>
                  {selected && <rect x={0} y={0} width={width} height={size.height} style={{ fill: 'var(--color-primary-soft)', opacity: 0.6 }} />}
                  <g
                    role="img"
                    aria-label={barLabel(task, memberName)}
                    className={canDrag(task) ? (drag?.taskId === task.id ? styles.dragging : styles.draggable) : undefined}
                    onPointerDown={(e) => onBarPointerDown(e, task)}
                    onPointerMove={(e) => onBarPointerMove(e, task)}
                    onPointerUp={() => onBarPointerUp(task)}
                    onPointerLeave={() => setTooltip(null)}
                  >
                    {p.start && p.end && (task.isMilestone ? (
                      <Milestone x={xOf(p.start, range.from, dw) + dw / 2} size={size} />
                    ) : task.isSummary ? (
                      <SummaryBar start={p.start} end={p.end} progress={task.progress} from={range.from} dw={dw} size={size} color={color(task)} />
                    ) : (
                      <PlanBar start={p.start} end={p.end} progress={task.progress} from={range.from} dw={dw} size={size} color={color(task)} />
                    ))}
                    <ActualBar task={task} today={today} from={range.from} dw={dw} size={size} />
                    <FlagMark task={task} from={range.from} dw={dw} size={size} today={today} />
                  </g>
                </svg>
              </div>
            );
          })}
        </div>
      </div>

      {tooltip && <Tooltip task={tooltip.task} x={tooltip.x} y={tooltip.y} memberName={memberName} />}
    </div>
  );
}

function GroupLabel({ row, top, height, onToggle }: { row: GroupRow; top: number; height: number; onToggle: (key: string) => void }) {
  return (
    <div className={[styles.row, styles.rowGroup].join(' ')} style={{ top, height }} role="row" aria-expanded={!row.collapsed}>
      <span className={[styles.cell, styles.nameCell].join(' ')}>
        <button type="button" className={styles.toggle} aria-label={row.collapsed ? '開く' : '閉じる'} onClick={() => onToggle(row.key)}>
          <Icon name={row.collapsed ? 'chevronRight' : 'chevronDown'} size={16} />
        </button>
        {row.status && <span aria-hidden="true">{STATUS[row.status].icon}</span>}
        {row.label}
        <span className={styles.groupMeta}>
          （{row.count}件）
          {row.plannedMinutesInRange !== undefined && ` 期間内の予定 ${formatHours(Math.round(row.plannedMinutesInRange * 10) / 10)}`}
          {row.bar && ` 進捗 ${row.bar.progress}%`}
        </span>
      </span>
    </div>
  );
}

function TaskLabel({
  row,
  top,
  height,
  selected,
  cols,
  memberName,
  tagById,
  onToggle,
  onOpen,
  onSelect,
}: {
  row: TaskRow;
  top: number;
  height: number;
  selected: boolean;
  cols: Column[];
  memberName: Map<string, string>;
  tagById: Map<string, Gantt['tags'][number]>;
  onToggle: (key: string) => void;
  onOpen: (id: string) => void;
  onSelect: (id: string) => void;
}) {
  const t = row.task;
  const variance = t.plannedMinutes !== null ? t.actualMinutes - t.plannedMinutes : null;
  const cell = (c: Column) => {
    switch (c) {
      case 'assignee':
        return t.assigneeId ? memberName.get(t.assigneeId) ?? '' : t.isSummary ? '' : '未割り当て';
      case 'status':
        return `${STATUS[t.status].icon} ${STATUS[t.status].label}`;
      case 'progress':
        return `${t.progress}%`;
      case 'planned_dates':
        return t.plannedStart ? `${formatShortDate(t.plannedStart)}〜${formatShortDate(t.plannedEnd)}` : '未定';
      case 'actual_dates':
        return t.actualStart ? `${formatShortDate(t.actualStart)}〜${formatShortDate(t.actualEnd)}` : '';
      case 'planned_minutes':
        return formatHours(t.plannedMinutes);
      case 'actual_minutes':
        return formatHours(t.actualMinutes);
      case 'variance':
        return formatSignedHours(variance);
      case 'priority':
        return PRIORITY[t.priority].label;
      case 'tags':
        return t.tagIds.map((id) => tagById.get(id)?.name).filter(Boolean).join('、');
    }
  };
  return (
    <div
      id={`gantt-row-${t.id}`}
      role="row"
      aria-selected={selected}
      aria-level={row.depth + 1}
      aria-expanded={row.hasChildren ? !row.collapsed : undefined}
      className={[styles.row, selected && styles.rowSelected, row.context && styles.rowContext].filter(Boolean).join(' ')}
      style={{ top, height }}
      onClick={() => onSelect(t.id)}
    >
      <span className={[styles.cell, styles.nameCell].join(' ')} style={{ paddingLeft: 8 + row.depth * 16 }}>
        {row.hasChildren ? (
          <button type="button" className={styles.toggle} aria-label={row.collapsed ? '子タスクを開く' : '子タスクを閉じる'} onClick={() => onToggle(row.key)}>
            <Icon name={row.collapsed ? 'chevronRight' : 'chevronDown'} size={16} />
          </button>
        ) : (
          <span className={styles.toggleSpacer} />
        )}
        {t.flags.length > 0 && (
          <span className={styles.flagIcon} title={t.flags.map((f) => FLAG[f].label).join('、')}>
            ！
          </span>
        )}
        <button
          type="button"
          tabIndex={-1}
          className={[styles.taskName, t.isSummary && styles.summaryName, t.status === 'cancelled' && styles.cancelled].filter(Boolean).join(' ')}
          onClick={() => onOpen(t.id)}
          title={t.title}
        >
          {t.isMilestone && '◆ '}
          {t.title}
        </button>
      </span>
      {cols.map((c) => (
        <span key={c} className={styles.cell} style={{ width: COLUMN_WIDTH[c] }} role="gridcell">
          {cell(c)}
        </span>
      ))}
    </div>
  );
}

function Background({ from, days, dw, height, holidays, todayX }: { from: string; days: number; dw: number; height: number; holidays: Map<number, string>; todayX: number | null }) {
  const start = toDay(from);
  const rects = [];
  for (let i = 0; i < days; i++) {
    const day = start + i;
    const holiday = holidays.has(day);
    if (isWeekend(day) || holiday) {
      rects.push(<rect key={i} x={i * dw} y={0} width={dw} height={height} style={{ fill: holiday ? 'var(--color-holiday)' : 'var(--color-weekend)' }} />);
    }
  }
  return (
    <svg className={styles.layer} width={days * dw} height={height} aria-hidden="true">
      {rects}
      {todayX !== null && <line x1={todayX + dw / 2} x2={todayX + dw / 2} y1={0} y2={height} style={{ stroke: 'var(--color-today)', strokeWidth: 2 }} />}
    </svg>
  );
}

type Size = (typeof ROW)['normal'] | (typeof ROW)['dense'];

function PlanBar({ start, end, progress, from, dw, size, color }: { start: string; end: string; progress: number; from: string; dw: number; size: Size; color: string }) {
  const x = xOf(start, from, dw);
  const w = widthOf(start, end, dw);
  return (
    <>
      <rect x={x} y={size.planY} width={w} height={size.planH} rx={2} style={{ fill: color, fillOpacity: 0.22, stroke: color, strokeWidth: 1 }} />
      {progress > 0 && <rect x={x} y={size.planY} width={(w * progress) / 100} height={size.planH} rx={2} style={{ fill: color }} />}
    </>
  );
}

function SummaryBar({ start, end, progress, from, dw, size, color }: { start: string; end: string; progress: number; from: string; dw: number; size: Size; color: string }) {
  const x = xOf(start, from, dw);
  const w = widthOf(start, end, dw);
  const y = size.planY + 2;
  const h = size.planH - 6;
  return (
    <>
      <rect x={x} y={y} width={w} height={h} style={{ fill: color, fillOpacity: 0.35 }} />
      <rect x={x} y={y} width={(w * progress) / 100} height={h} style={{ fill: color }} />
      <path d={`M${x} ${y} v${h + 5} l5 -5 z M${x + w} ${y} v${h + 5} l-5 -5 z`} style={{ fill: color }} />
    </>
  );
}

function Milestone({ x, size }: { x: number; size: Size }) {
  const half = size.diamond / 2;
  const cy = size.planY + size.planH / 2;
  return <path d={`M${x} ${cy - half} L${x + half} ${cy} L${x} ${cy + half} L${x - half} ${cy} Z`} style={{ fill: 'var(--color-milestone)' }} />;
}

function actualEndOf(task: GanttTask, today: string): string | null {
  if (!task.actualStart) return null;
  const end = task.actualEnd ?? (task.status === 'done' ? task.actualStart : today);
  return end < task.actualStart ? task.actualStart : end;
}

function ActualBar({ task, today, from, dw, size }: { task: GanttTask; today: string; from: string; dw: number; size: Size }) {
  const end = actualEndOf(task, today);
  if (!task.actualStart || !end || task.isMilestone) return null;
  const x = xOf(task.actualStart, from, dw);
  const w = widthOf(task.actualStart, end, dw);
  const running = !task.actualEnd && task.status !== 'done';
  return (
    <>
      <rect x={x} y={size.actualY} width={w} height={size.actualH} style={{ fill: 'var(--color-actual-bar)' }} />
      {running && <path d={`M${x + w} ${size.actualY - 1} l5 ${size.actualH / 2 + 1} l-5 ${size.actualH / 2 + 1} z`} style={{ fill: 'var(--color-actual-bar)' }} />}
    </>
  );
}

/** 遅れ・超過の印（色に加えてアイコンでも強調する。FR-GNT-07）。 */
function FlagMark({ task, from, dw, size, today }: { task: GanttTask; from: string; dw: number; size: Size; today: string }) {
  if (task.flags.length === 0 && !task.descendantFlagged) return null;
  const ends = [task.plannedEnd, actualEndOf(task, today)].filter((d): d is string => !!d).sort();
  const last = ends[ends.length - 1];
  if (!last) return null;
  const x = xOf(last, from, dw) + dw + 8;
  const cy = size.planY + size.planH / 2;
  const own = task.flags.length > 0;
  return (
    <g>
      <circle cx={x} cy={cy} r={own ? 7 : 5} style={{ fill: own ? 'var(--color-danger)' : 'var(--color-warning)' }} />
      <text x={x} y={cy + 4} textAnchor="middle" style={{ fill: '#ffffff', fontSize: own ? 11 : 9, fontWeight: 700 }}>
        !
      </text>
    </g>
  );
}

/** 依存関係の矢印（FR-GNT-12）。先行タスクの予定終了から、後続タスクの予定開始へ。 */
function DependencyArrows({ rows, rowIndex, from, dw, size, width, height }: { rows: GanttRow[]; rowIndex: Map<string, number>; from: string; dw: number; size: Size; width: number; height: number }) {
  const paths: string[] = [];
  for (const row of rows) {
    if (row.kind !== 'task' || !row.task.plannedStart) continue;
    const to = rowIndex.get(row.task.id)!;
    for (const predId of row.task.predecessorIds) {
      const fromIndex = rowIndex.get(predId);
      const pred = fromIndex !== undefined ? rows[fromIndex] : undefined;
      if (!pred || pred.kind !== 'task' || !pred.task.plannedEnd) continue;
      const x1 = xOf(pred.task.plannedEnd, from, dw) + dw;
      const y1 = fromIndex! * size.height + size.planY + size.planH / 2;
      const x2 = xOf(row.task.plannedStart, from, dw);
      const y2 = to * size.height + size.planY + size.planH / 2;
      paths.push(`M${x1} ${y1} h6 V${y2} H${x2 - 2}`);
    }
  }
  if (paths.length === 0) return null;
  return (
    <svg className={styles.layer} width={width} height={height} aria-hidden="true">
      <defs>
        <marker id="gantt-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0 0 L8 4 L0 8 z" style={{ fill: 'var(--color-text-muted)' }} />
        </marker>
      </defs>
      {paths.map((d, i) => (
        <path key={i} d={d} markerEnd="url(#gantt-arrow)" style={{ fill: 'none', stroke: 'var(--color-text-muted)', strokeWidth: 1.2 }} />
      ))}
    </svg>
  );
}

function barLabel(task: GanttTask, memberName: Map<string, string>): string {
  const assignee = task.assigneeId ? memberName.get(task.assigneeId) ?? '' : '未割り当て';
  return `${task.title}、${STATUS[task.status].label}、担当 ${assignee}、進捗 ${task.progress}%`;
}

/** バーの要点（担当者、日程、工数、進捗、状態）。文字列として表示する（HTML として解釈しない。NF-INP-03）。 */
function Tooltip({ task, x, y, memberName }: { task: GanttTask; x: number; y: number; memberName: Map<string, string> }) {
  const assignee = task.assigneeId ? memberName.get(task.assigneeId) ?? '' : '未割り当て';
  return (
    <div className={styles.tooltip} style={{ position: 'fixed', left: Math.min(x + 14, window.innerWidth - 330), top: y + 14 }} role="tooltip">
      <strong>{task.title}</strong>
      <div>
        担当: {assignee}　状態: {STATUS[task.status].icon} {STATUS[task.status].label}
      </div>
      <div>
        予定: {task.plannedStart ? `${formatShortDate(task.plannedStart)}〜${formatShortDate(task.plannedEnd)}` : '未定'}　{formatHours(task.plannedMinutes, '-')}
      </div>
      <div>
        実績: {task.actualStart ? `${formatShortDate(task.actualStart)}〜${task.actualEnd ? formatShortDate(task.actualEnd) : '（進行中）'}` : '-'}　{formatHours(task.actualMinutes)}
      </div>
      <div>
        進捗: {task.progress}%{task.expectedProgress !== null && `（期待 ${task.expectedProgress}%）`}
      </div>
      {task.flags.length > 0 && <div>！ {task.flags.map((f) => FLAG[f].label).join('、')}</div>}
    </div>
  );
}

