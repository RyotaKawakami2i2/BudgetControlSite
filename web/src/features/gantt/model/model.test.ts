import { describe, expect, it } from 'vitest';
import type { GanttTask } from '../../../api/types';
import { applyQuickFilter, clearFilters, DEFAULT_CONDITIONS, hasFilters, fromSearchParams, fromViewConditions, isQuickFilterOn, toSearchParams, toViewConditions } from './conditions';
import { buildRows, matches, teamBar, type RowInput } from './rows';
import { ticks, widthOf, xOf } from './scale';

const TEAM = '00000000-0000-0000-0000-00000000000a';
const ME = '00000000-0000-0000-0000-0000000000a1';
const OTHER = '00000000-0000-0000-0000-0000000000a2';

function task(id: string, partial: Partial<GanttTask> = {}): GanttTask {
  return {
    id,
    teamId: TEAM,
    parentId: null,
    depth: 1,
    sortOrder: 1024,
    title: id,
    assigneeId: null,
    createdById: ME,
    status: 'not_started',
    priority: 'medium',
    isMilestone: false,
    isSummary: false,
    plannedStart: '2026-10-05',
    plannedEnd: '2026-10-09',
    plannedMinutes: 600,
    actualStart: null,
    actualEnd: null,
    actualMinutes: 0,
    progress: 0,
    expectedProgress: 0,
    flags: [],
    descendantFlagged: false,
    tagIds: [],
    predecessorIds: [],
    version: 1,
    can: { editPlan: true, assign: true, editActual: true, logWork: true, addChild: true, delete: true },
    ...partial,
  };
}

function input(tasks: GanttTask[]): RowInput {
  return {
    tasks,
    teams: [{ id: TEAM, name: 'チーム A', role: 'leader', archived: false }],
    members: [
      { id: ME, displayName: '山田', teamIds: [TEAM], current: true, disabled: false },
      { id: OTHER, displayName: '鈴木', teamIds: [TEAM], current: true, disabled: false },
    ],
    meId: ME,
    today: '2026-10-07',
    range: { from: '2026-10-05', to: '2026-10-11' },
    holidays: new Set(),
    searchIds: null,
  };
}

// 設計（まとめ） > 画面設計（自分）、API 設計（鈴木、遅れ）
const tasks = [
  task('design', { isSummary: true, plannedMinutes: 1200 }),
  task('screens', { parentId: 'design', depth: 2, assigneeId: ME, sortOrder: 1024 }),
  task('api', { parentId: 'design', depth: 2, assigneeId: OTHER, sortOrder: 2048, flags: ['overdue'], plannedEnd: '2026-10-06', priority: 'high' }),
  task('release', { isMilestone: true, plannedStart: '2026-10-20', plannedEnd: '2026-10-20', plannedMinutes: null, sortOrder: 2048 }),
];

describe('表示条件と URL（FR-GNT-25）', () => {
  it('既定値は URL に書かない', () => {
    expect(toSearchParams(DEFAULT_CONDITIONS).toString()).toBe('');
  });

  it('URL を往復しても同じ条件になる', () => {
    const c = { ...DEFAULT_CONDITIONS, teams: [TEAM], group: 'assignee' as const, assignees: ['me', 'unassigned'], flags: ['overdue' as const], zoom: 'week' as const, q: 'API', dense: true };
    expect(fromSearchParams(toSearchParams(c))).toEqual(c);
  });

  it('正しくない値は無視する', () => {
    const c = fromSearchParams(new URLSearchParams('group=evil&status=done,bogus&teams=not-a-uuid&zoom=week'));
    expect(c.group).toBe('hierarchy');
    expect(c.status).toEqual(DEFAULT_CONDITIONS.status);
    expect(c.teams).toEqual([]);
    expect(c.zoom).toBe('week');
  });

  it('ビューには task 以外の項目を保存し、読み直せる', () => {
    const c = { ...DEFAULT_CONDITIONS, range: 'custom' as const, from: '2026-10-01', to: '2026-12-31', sort: 'planned_end' as const };
    const view = toViewConditions(c);
    expect(view.v).toBe(1);
    expect(fromViewConditions(view)).toEqual(c);
  });

  it('よく使う条件', () => {
    const on = applyQuickFilter(DEFAULT_CONDITIONS, 'delayed', true);
    expect(on.flags).toEqual(['overdue', 'late_start']);
    expect(isQuickFilterOn(on, 'delayed')).toBe(true);
    expect(isQuickFilterOn(applyQuickFilter(on, 'delayed', false), 'delayed')).toBe(false);
  });
});

describe('行の組み立て（詳細設計書 7.3.1）', () => {
  it('階層: 親の後に子を並び順で並べる', () => {
    const { rows } = buildRows(input(tasks), DEFAULT_CONDITIONS, new Set());
    expect(rows.map((r) => r.key)).toEqual(['t:design', 't:screens', 't:api', 't:release']);
  });

  it('折りたたむと子を出さない', () => {
    const { rows } = buildRows(input(tasks), DEFAULT_CONDITIONS, new Set(['t:design']));
    expect(rows.map((r) => r.key)).toEqual(['t:design', 't:release']);
  });

  it('絞り込むと、条件に合うタスクの祖先を薄く表示する', () => {
    const { rows } = buildRows(input(tasks), { ...DEFAULT_CONDITIONS, flags: ['overdue'] }, new Set());
    expect(rows.map((r) => [r.key, r.kind === 'task' && r.context])).toEqual([
      ['t:design', true],
      ['t:api', false],
    ]);
  });

  it('「自分」と「未割り当て」で絞り込める', () => {
    const mine = buildRows(input(tasks), { ...DEFAULT_CONDITIONS, assignees: ['me'] }, new Set());
    expect(mine.rows.filter((r) => r.kind === 'task' && !r.context).map((r) => r.key)).toEqual(['t:screens']);
    expect(matches(tasks[3]!, { ...DEFAULT_CONDITIONS, assignees: ['unassigned'] }, input(tasks))).toBe(true);
  });

  it('担当者別: 担当者ごとに見出しを付け、表示期間内の予定工数の合計を出す', () => {
    const { rows } = buildRows(input(tasks), { ...DEFAULT_CONDITIONS, group: 'assignee' }, new Set());
    const groups = rows.filter((r) => r.kind === 'group');
    expect(groups.map((g) => g.label)).toEqual(['山田', '鈴木', '未割り当て']);
    // 画面設計: 10/5〜10/9 の 5 稼働日で 600 分。表示期間 10/5〜10/11 に全部入る
    expect(groups[0]!.kind === 'group' && groups[0]!.plannedMinutesInRange).toBe(600);
  });

  it('状態別とキーワード', () => {
    const byStatus = buildRows(input(tasks), { ...DEFAULT_CONDITIONS, group: 'status' }, new Set());
    expect(byStatus.rows[0]).toMatchObject({ kind: 'group', label: '未着手', count: 3 });
    const searched = buildRows({ ...input(tasks), searchIds: new Set(['api']) }, { ...DEFAULT_CONDITIONS, q: 'API' }, new Set());
    expect(searched.rows.filter((r) => r.kind === 'task' && !r.context).map((r) => r.key)).toEqual(['t:api']);
  });

  it('合計は子を持たないタスクだけを数える（FR-GNT-28）', () => {
    const { totals } = buildRows(input(tasks), DEFAULT_CONDITIONS, new Set());
    expect(totals).toEqual({ count: 3, plannedMinutes: 1200, actualMinutes: 0 });
  });

  it('並べ替え（優先度）', () => {
    const { rows } = buildRows(input(tasks), { ...DEFAULT_CONDITIONS, sort: 'priority' }, new Set());
    expect(rows.map((r) => r.key)).toEqual(['t:design', 't:api', 't:screens', 't:release']);
  });

  it('チームのバー', () => {
    expect(teamBar(tasks, TEAM)).toEqual({ start: '2026-10-05', end: '2026-10-20', progress: 0 });
  });
});

describe('時間軸', () => {
  it('座標と幅', () => {
    expect(xOf('2026-10-07', '2026-10-05', 32)).toBe(64);
    expect(widthOf('2026-10-05', '2026-10-05', 32)).toBe(32);
    expect(widthOf('2026-10-05', '2026-10-05', 1.5)).toBe(2);
  });

  it('日の目盛りは、月と日の 2 段', () => {
    const { upper, lower } = ticks('2026-09-29', '2026-10-02', 'day');
    expect(upper.map((t) => t.label)).toEqual(['2026/9', '2026/10']);
    expect(lower.map((t) => t.label)).toEqual(['29', '30', '1', '2']);
    expect(upper[0]!.width).toBe(64);
  });
});

describe('絞り込みの解除', () => {
  it('絞り込みだけを解除し、チームや表示の条件は残す', () => {
    const c = { ...DEFAULT_CONDITIONS, teams: ['t1'], group: 'assignee' as const, zoom: 'week' as const, assignees: ['me'], flags: ['overdue' as const], q: '設計', milestones: true };
    const cleared = clearFilters(c);

    expect(hasFilters(c)).toBe(true);
    expect(hasFilters(cleared)).toBe(false);
    expect(cleared.teams).toEqual(['t1']);
    expect(cleared.group).toBe('assignee');
    expect(cleared.zoom).toBe('week');
  });
});

