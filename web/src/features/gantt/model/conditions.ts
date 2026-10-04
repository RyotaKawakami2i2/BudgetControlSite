/**
 * ガントの表示条件（詳細設計書 7.3.6）。表示条件は URL に持たせ、URL を共有すると同じ表示を再現できる（FR-GNT-25）。
 * ビューには、task 以外の項目を JSON にしたものを保存する（FR-GNT-26）。
 */
import { z } from 'zod';
import type { DelayFlag, Priority, TaskStatus } from '../../../api/types';
import type { RangePreset } from '../../../lib/dates';
import { ALL_COLUMNS, type ColorBy, type Column, type GroupBy, type SortBy, type Zoom } from '../../../lib/labels';

export type Due = 'this_week';

export interface GanttConditions {
  teams: string[];
  range: RangePreset;
  from: string | null;
  to: string | null;
  group: GroupBy;
  sort: SortBy;
  assignees: string[];
  status: TaskStatus[];
  priority: Priority[];
  tags: string[];
  flags: DelayFlag[];
  milestones: boolean;
  due: Due | null;
  q: string;
  zoom: Zoom;
  cols: Column[];
  color: ColorBy;
  dense: boolean;
}

export const DEFAULT_COLUMNS: Column[] = ['assignee', 'status', 'progress'];

export const DEFAULT_CONDITIONS: GanttConditions = {
  teams: [],
  range: 'default',
  from: null,
  to: null,
  group: 'hierarchy',
  sort: 'manual',
  assignees: [],
  status: [],
  priority: [],
  tags: [],
  flags: [],
  milestones: false,
  due: null,
  q: '',
  zoom: 'day',
  cols: DEFAULT_COLUMNS,
  color: 'status',
  dense: false,
};

const uuid = z.string().regex(/^[0-9a-fA-F-]{36}$/);
const date = z.string().regex(/^\d{4}-\d{2}-\d{2}$/);
const statusCode = z.enum(['not_started', 'in_progress', 'on_hold', 'done', 'cancelled']);
const priorityCode = z.enum(['high', 'medium', 'low']);
const flagCode = z.enum(['overdue', 'late_start', 'effort_overrun', 'progress_lag']);
const columnCode = z.enum(ALL_COLUMNS as [Column, ...Column[]]);

/** ビューの条件の形式（サーバーでも同じ規則で検査する）。 */
const viewSchema = z.object({
  v: z.literal(1).optional(),
  teams: z.array(uuid).max(50).optional(),
  range: z.enum(['default', 'this_month', 'next_month', 'this_quarter', 'custom']).optional(),
  from: date.optional(),
  to: date.optional(),
  group: z.enum(['hierarchy', 'assignee', 'team', 'status']).optional(),
  sort: z.enum(['manual', 'planned_start', 'planned_end', 'priority', 'progress']).optional(),
  assignees: z.array(z.union([z.literal('me'), z.literal('unassigned'), uuid])).max(50).optional(),
  status: z.array(statusCode).max(50).optional(),
  priority: z.array(priorityCode).max(50).optional(),
  tags: z.array(uuid).max(50).optional(),
  flags: z.array(flagCode).max(50).optional(),
  milestones: z.boolean().optional(),
  due: z.literal('this_week').optional(),
  q: z.string().max(100).optional(),
  zoom: z.enum(['day', 'week', 'month', 'quarter']).optional(),
  cols: z.array(columnCode).max(50).optional(),
  color: z.enum(['status', 'assignee', 'priority', 'tag']).optional(),
  dense: z.boolean().optional(),
});

function list(params: URLSearchParams, key: string): string[] {
  const value = params.get(key);
  return value ? value.split(',').filter(Boolean) : [];
}

/** URL の項目を読む。正しくない値は無視して既定値にする。 */
export function fromSearchParams(params: URLSearchParams): GanttConditions {
  const raw: Record<string, unknown> = {
    teams: list(params, 'teams'),
    range: params.get('range') ?? undefined,
    from: params.get('from') ?? undefined,
    to: params.get('to') ?? undefined,
    group: params.get('group') ?? undefined,
    sort: params.get('sort') ?? undefined,
    assignees: list(params, 'assignees'),
    status: list(params, 'status'),
    priority: list(params, 'priority'),
    tags: list(params, 'tags'),
    flags: list(params, 'flags'),
    milestones: params.get('milestones') === '1' ? true : undefined,
    due: params.get('due') ?? undefined,
    q: params.get('q') ?? undefined,
    zoom: params.get('zoom') ?? undefined,
    cols: params.has('cols') ? list(params, 'cols') : undefined,
    color: params.get('color') ?? undefined,
    dense: params.get('dense') === '1' ? true : undefined,
  };
  return fromViewConditions(raw);
}

/** ビューの条件（JSON）を読む。項目ごとに検査し、正しくない項目は既定値にする。 */
export function fromViewConditions(raw: Record<string, unknown>): GanttConditions {
  const result: GanttConditions = { ...DEFAULT_CONDITIONS };
  const shape = viewSchema.shape;
  for (const key of Object.keys(shape) as Array<keyof typeof shape>) {
    if (key === 'v' || raw[key] === undefined) continue;
    const parsed = shape[key].safeParse(raw[key]);
    if (parsed.success && parsed.data !== undefined) {
      (result as unknown as Record<string, unknown>)[key] = parsed.data;
    }
  }
  return result;
}

/** 既定値と違う項目だけを URL に書く。 */
export function toSearchParams(c: GanttConditions, extra: Record<string, string | null> = {}): URLSearchParams {
  const params = new URLSearchParams();
  const setList = (key: string, values: string[]) => values.length > 0 && params.set(key, values.join(','));
  setList('teams', c.teams);
  if (c.range !== 'default') params.set('range', c.range);
  if (c.range === 'custom' && c.from) params.set('from', c.from);
  if (c.range === 'custom' && c.to) params.set('to', c.to);
  if (c.group !== 'hierarchy') params.set('group', c.group);
  if (c.sort !== 'manual') params.set('sort', c.sort);
  setList('assignees', c.assignees);
  setList('status', c.status);
  setList('priority', c.priority);
  setList('tags', c.tags);
  setList('flags', c.flags);
  if (c.milestones) params.set('milestones', '1');
  if (c.due) params.set('due', c.due);
  if (c.q.trim()) params.set('q', c.q.trim());
  if (c.zoom !== 'day') params.set('zoom', c.zoom);
  if (c.cols.join(',') !== DEFAULT_COLUMNS.join(',')) params.set('cols', c.cols.join(','));
  if (c.color !== 'status') params.set('color', c.color);
  if (c.dense) params.set('dense', '1');
  for (const [key, value] of Object.entries(extra)) {
    if (value) params.set(key, value);
  }
  return params;
}

/** ビューに保存する形（task 以外の項目）。 */
export function toViewConditions(c: GanttConditions): Record<string, unknown> {
  const view: Record<string, unknown> = { v: 1 };
  for (const [key, value] of Object.entries(c)) {
    if (value === null || value === '' || value === false) continue;
    if (Array.isArray(value) && value.length === 0) continue;
    if ((key === 'from' || key === 'to') && c.range !== 'custom') continue;
    view[key] = value;
  }
  return view;
}

/** 絞り込みの条件が 1 つでもあるか。 */
export function hasFilters(c: GanttConditions): boolean {
  return (
    c.assignees.length > 0 ||
    c.status.length > 0 ||
    c.priority.length > 0 ||
    c.tags.length > 0 ||
    c.flags.length > 0 ||
    c.milestones ||
    c.due !== null ||
    c.q.trim().length > 0
  );
}

/** よく使う条件（FR-GNT-21）。 */
export type QuickFilter = 'mine' | 'due_this_week' | 'delayed' | 'unassigned';

export function applyQuickFilter(c: GanttConditions, quick: QuickFilter, on: boolean): GanttConditions {
  switch (quick) {
    case 'mine':
      return { ...c, assignees: on ? ['me'] : c.assignees.filter((a) => a !== 'me') };
    case 'unassigned':
      return { ...c, assignees: on ? ['unassigned'] : c.assignees.filter((a) => a !== 'unassigned') };
    case 'due_this_week':
      return { ...c, due: on ? 'this_week' : null };
    case 'delayed':
      return { ...c, flags: on ? ['overdue', 'late_start'] : [] };
  }
}

export function isQuickFilterOn(c: GanttConditions, quick: QuickFilter): boolean {
  switch (quick) {
    case 'mine':
      return c.assignees.length === 1 && c.assignees[0] === 'me';
    case 'unassigned':
      return c.assignees.length === 1 && c.assignees[0] === 'unassigned';
    case 'due_this_week':
      return c.due === 'this_week';
    case 'delayed':
      return c.flags.length === 2 && c.flags.includes('overdue') && c.flags.includes('late_start');
  }
}
