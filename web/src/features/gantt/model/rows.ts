/**
 * ガントの行の組み立て（詳細設計書 7.3.1〜7.3.2、7.3.7）。純粋な関数とし、絞り込み → まとめ方 → 並べ替え → 折りたたみの順に行う。
 * 取得済みのデータに対して画面側で行い、1 秒以内に反映する（NF-PRF-03）。
 */
import type { GanttMember, GanttTask, GanttTeam, TaskStatus } from '../../../api/types';
import { mondayOf, proratedMinutes, toDay } from '../../../lib/dates';
import { PRIORITY, STATUS, STATUS_ORDER } from '../../../lib/labels';
import { hasFilters, type GanttConditions } from './conditions';

export interface GroupRow {
  kind: 'group';
  key: string;
  label: string;
  count: number;
  collapsed: boolean;
  /** 担当者別: 表示期間内の予定工数の合計（分）。 */
  plannedMinutesInRange?: number;
  /** チーム別: プロジェクト全体の期間と進捗。 */
  bar?: { start: string; end: string; progress: number };
  status?: TaskStatus;
}

export interface TaskRow {
  kind: 'task';
  key: string;
  task: GanttTask;
  depth: number;
  /** 条件に合うタスクの祖先として、薄く表示する行。 */
  context: boolean;
  hasChildren: boolean;
  collapsed: boolean;
}

export type GanttRow = GroupRow | TaskRow;

export interface RowInput {
  tasks: GanttTask[];
  teams: GanttTeam[];
  members: GanttMember[];
  meId: string;
  today: string;
  range: { from: string; to: string };
  holidays: ReadonlySet<number>;
  /** キーワード検索で一致したタスクの ID（検索していなければ null）。 */
  searchIds: ReadonlySet<string> | null;
}

export interface RowResult {
  rows: GanttRow[];
  /** 表示中のタスク（子を持たないもの。中止を除く）の件数と工数の合計（FR-GNT-28）。 */
  totals: { count: number; plannedMinutes: number; actualMinutes: number };
}

/** 1 件のタスクが、絞り込みの条件に合うか。 */
export function matches(task: GanttTask, c: GanttConditions, input: Pick<RowInput, 'meId' | 'today' | 'searchIds'>): boolean {
  if (c.assignees.length > 0) {
    const ok = c.assignees.some((a) =>
      a === 'me' ? task.assigneeId === input.meId : a === 'unassigned' ? task.assigneeId === null : task.assigneeId === a,
    );
    if (!ok) return false;
  }
  if (c.status.length > 0 && !c.status.includes(task.status)) return false;
  if (c.priority.length > 0 && !c.priority.includes(task.priority)) return false;
  if (c.tags.length > 0 && !task.tagIds.some((t) => c.tags.includes(t))) return false;
  if (c.flags.length > 0 && !task.flags.some((f) => c.flags.includes(f))) return false;
  if (c.milestones && !task.isMilestone) return false;
  if (c.due === 'this_week') {
    const monday = mondayOf(input.today);
    const sunday = toDay(monday) + 6;
    if (!task.plannedEnd || task.status === 'done' || task.status === 'cancelled') return false;
    const end = toDay(task.plannedEnd);
    if (end < toDay(monday) || end > sunday) return false;
  }
  if (c.q.trim() && input.searchIds && !input.searchIds.has(task.id)) return false;
  return true;
}

function compare(a: GanttTask, b: GanttTask, c: GanttConditions, order: Map<string, number>): number {
  const byOrder = (order.get(a.id) ?? 0) - (order.get(b.id) ?? 0);
  const nullsLast = (x: string | null, y: string | null) => (x === y ? 0 : x === null ? 1 : y === null ? -1 : x < y ? -1 : 1);
  switch (c.sort) {
    case 'planned_start':
      return nullsLast(a.plannedStart, b.plannedStart) || byOrder;
    case 'planned_end':
      return nullsLast(a.plannedEnd, b.plannedEnd) || byOrder;
    case 'priority':
      return PRIORITY[a.priority].rank - PRIORITY[b.priority].rank || byOrder;
    case 'progress':
      return a.progress - b.progress || byOrder;
    default:
      return byOrder;
  }
}

export function buildRows(input: RowInput, c: GanttConditions, collapsed: ReadonlySet<string>): RowResult {
  const tasks = input.tasks;
  const byId = new Map(tasks.map((t) => [t.id, t]));
  // サーバーは階層の順（親の後に子、並び順）で返す。その順番を「手動の並び順」として使う
  const order = new Map(tasks.map((t, i) => [t.id, i]));
  const children = new Map<string | null, GanttTask[]>();
  for (const t of tasks) {
    const parent = t.parentId && byId.has(t.parentId) ? t.parentId : null;
    const key = parent ?? `root:${t.teamId}`;
    const list = children.get(key) ?? [];
    list.push(t);
    children.set(key, list);
  }

  const filtering = hasFilters(c);
  const matched = new Set(tasks.filter((t) => !filtering || matches(t, c, input)).map((t) => t.id));

  // 条件に合うタスクの祖先も、どこに属するタスクかが分かるように表示する（階層とチーム別）
  const visible = new Set<string>();
  for (const id of matched) {
    let current = byId.get(id);
    while (current) {
      if (visible.has(current.id)) break;
      visible.add(current.id);
      current = current.parentId ? byId.get(current.parentId) : undefined;
    }
  }

  const totals = { count: 0, plannedMinutes: 0, actualMinutes: 0 };
  for (const id of matched) {
    const t = byId.get(id)!;
    if (t.isSummary || t.status === 'cancelled') continue;
    totals.count++;
    totals.plannedMinutes += t.plannedMinutes ?? 0;
    totals.actualMinutes += t.actualMinutes;
  }

  const rows: GanttRow[] = [];

  const pushTree = (parentKey: string, depthOffset: number) => {
    const kids = (children.get(parentKey) ?? []).filter((t) => visible.has(t.id)).sort((a, b) => compare(a, b, c, order));
    for (const t of kids) {
      const kidCount = (children.get(t.id) ?? []).filter((k) => visible.has(k.id)).length;
      const key = `t:${t.id}`;
      const isCollapsed = collapsed.has(key);
      rows.push({
        kind: 'task',
        key,
        task: t,
        depth: t.depth - 1 + depthOffset,
        context: filtering && !matched.has(t.id),
        hasChildren: kidCount > 0,
        collapsed: isCollapsed,
      });
      if (!isCollapsed) pushTree(t.id, depthOffset);
    }
  };

  const flat = (list: GanttTask[]) =>
    list.filter((t) => matched.has(t.id) && !t.isSummary).sort((a, b) => compare(a, b, c, order));

  const pushFlat = (list: GanttTask[]) => {
    for (const t of list) {
      rows.push({ kind: 'task', key: `t:${t.id}`, task: t, depth: 1, context: false, hasChildren: false, collapsed: false });
    }
  };

  switch (c.group) {
    case 'hierarchy':
      for (const team of input.teams) {
        if (input.teams.length > 1) {
          // 複数のチームを並べるときは、チームの見出しを付ける
          const key = `g:team:${team.id}`;
          const teamTasks = tasks.filter((t) => t.teamId === team.id && visible.has(t.id));
          rows.push({ kind: 'group', key, label: team.name, count: teamTasks.filter((t) => matched.has(t.id) && !t.isSummary).length, collapsed: collapsed.has(key), bar: teamBar(tasks, team.id) });
          if (collapsed.has(key)) continue;
          pushTree(`root:${team.id}`, 1);
        } else {
          pushTree(`root:${team.id}`, 0);
        }
      }
      break;

    case 'team':
      for (const team of input.teams) {
        const key = `g:team:${team.id}`;
        const teamTasks = tasks.filter((t) => t.teamId === team.id && visible.has(t.id));
        rows.push({
          kind: 'group',
          key,
          label: team.name,
          count: teamTasks.filter((t) => matched.has(t.id) && !t.isSummary).length,
          collapsed: collapsed.has(key),
          bar: teamBar(tasks, team.id),
        });
        if (!collapsed.has(key)) pushTree(`root:${team.id}`, 1);
      }
      break;

    case 'assignee': {
      const groups = new Map<string, GanttTask[]>();
      for (const t of flat(tasks)) {
        const key = t.assigneeId ?? '';
        groups.set(key, [...(groups.get(key) ?? []), t]);
      }
      const memberById = new Map(input.members.map((m) => [m.id, m]));
      const keysSorted = [...groups.keys()].sort((a, b) => {
        if (a === '') return 1;
        if (b === '') return -1;
        return (memberById.get(a)?.displayName ?? '').localeCompare(memberById.get(b)?.displayName ?? '', 'ja');
      });
      for (const assignee of keysSorted) {
        const list = groups.get(assignee)!;
        const key = `g:assignee:${assignee || 'none'}`;
        const member = memberById.get(assignee);
        // 担当者ごとに、表示期間内の予定工数の合計を出す（表示しているチームの分だけ。詳細設計書 7.3.7）
        const planned = list
          .filter((t) => t.status !== 'cancelled')
          .reduce((sum, t) => sum + proratedMinutes(t.plannedStart, t.plannedEnd, t.plannedMinutes, input.range.from, input.range.to, input.holidays), 0);
        rows.push({
          kind: 'group',
          key,
          label: assignee === '' ? '未割り当て' : `${member?.displayName ?? '（不明）'}${member?.disabled ? '（無効）' : ''}`,
          count: list.length,
          collapsed: collapsed.has(key),
          plannedMinutesInRange: planned,
        });
        if (!collapsed.has(key)) pushFlat(list);
      }
      break;
    }

    case 'status':
      for (const status of STATUS_ORDER) {
        const list = flat(tasks).filter((t) => t.status === status);
        if (list.length === 0) continue;
        const key = `g:status:${status}`;
        rows.push({ kind: 'group', key, label: STATUS[status].label, count: list.length, collapsed: collapsed.has(key), status });
        if (!collapsed.has(key)) pushFlat(list);
      }
      break;
  }

  return { rows, totals };
}

/** チームの行のバー（プロジェクト全体の期間と進捗。進捗は根のタスクを予定工数で重み付けした平均）。 */
export function teamBar(tasks: GanttTask[], teamId: string): { start: string; end: string; progress: number } | undefined {
  const ids = new Set(tasks.filter((t) => t.teamId === teamId).map((t) => t.id));
  const roots = tasks.filter((t) => t.teamId === teamId && (!t.parentId || !ids.has(t.parentId)) && t.status !== 'cancelled');
  const starts = roots.map((t) => t.plannedStart).filter((d): d is string => !!d).sort();
  const ends = roots.map((t) => t.plannedEnd).filter((d): d is string => !!d).sort();
  if (starts.length === 0 || ends.length === 0) return undefined;
  let weight = 0;
  let weighted = 0;
  for (const t of roots) {
    const w = t.plannedMinutes && t.plannedMinutes > 0 ? t.plannedMinutes : 1;
    weight += w;
    weighted += w * t.progress;
  }
  return { start: starts[0]!, end: ends[ends.length - 1]!, progress: weight === 0 ? 0 : Math.floor(weighted / weight) };
}
