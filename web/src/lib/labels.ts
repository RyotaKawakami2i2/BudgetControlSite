/**
 * 区分値と画面の表示名の対応（基本設計書 5.3。DEV-04）。画面側ではここだけに持つ。
 * 状態や遅れは色だけで示さず、アイコンと文字を添える（基本設計書 4.4）。
 */
import type { DelayFlag, NotificationKind, Priority, TagColor, TaskStatus, TeamRoleCode } from '../api/types';

export const STATUS: Record<TaskStatus, { label: string; icon: string; color: string }> = {
  not_started: { label: '未着手', icon: '○', color: 'var(--color-status-not-started)' },
  in_progress: { label: '進行中', icon: '▶', color: 'var(--color-status-in-progress)' },
  on_hold: { label: '保留', icon: '‖', color: 'var(--color-status-on-hold)' },
  done: { label: '完了', icon: '✓', color: 'var(--color-status-done)' },
  cancelled: { label: '中止', icon: '×', color: 'var(--color-status-cancelled)' },
};

export const STATUS_ORDER: TaskStatus[] = ['not_started', 'in_progress', 'on_hold', 'done', 'cancelled'];

export const PRIORITY: Record<Priority, { label: string; rank: number; color: string }> = {
  high: { label: '高', rank: 0, color: 'var(--color-danger)' },
  medium: { label: '中', rank: 1, color: 'var(--color-primary)' },
  low: { label: '低', rank: 2, color: 'var(--color-text-muted)' },
};

export const PRIORITY_ORDER: Priority[] = ['high', 'medium', 'low'];

export const ROLE: Record<TeamRoleCode, string> = {
  leader: 'リーダー',
  member: 'メンバー',
  admin_view: '閲覧のみ（管理者）',
};

export const FLAG: Record<DelayFlag, { label: string; short: string }> = {
  overdue: { label: '期限超過', short: '期限' },
  late_start: { label: '開始遅れ', short: '開始' },
  effort_overrun: { label: '工数超過', short: '工数' },
  progress_lag: { label: '進捗遅れ', short: '進捗' },
};

export const FLAG_ORDER: DelayFlag[] = ['overdue', 'late_start', 'effort_overrun', 'progress_lag'];

export const TAG_COLORS: TagColor[] = ['gray', 'blue', 'green', 'yellow', 'orange', 'red', 'purple', 'teal'];

export const TAG_COLOR_LABEL: Record<TagColor, string> = {
  gray: '灰',
  blue: '青',
  green: '緑',
  yellow: '黄',
  orange: '橙',
  red: '赤',
  purple: '紫',
  teal: '青緑',
};

/** 担当者・タグで色分けするときの色（タグの 8 色の文字の色を使う）。 */
export const SERIES_COLORS = [
  'var(--tag-blue-fg)',
  'var(--tag-green-fg)',
  'var(--tag-purple-fg)',
  'var(--tag-orange-fg)',
  'var(--tag-teal-fg)',
  'var(--tag-red-fg)',
  'var(--tag-yellow-fg)',
  'var(--tag-gray-fg)',
];

export function tagColorVar(color: TagColor): { bg: string; fg: string } {
  return { bg: `var(--tag-${color}-bg)`, fg: `var(--tag-${color}-fg)` };
}

export const NOTIFICATION: Record<NotificationKind, string> = {
  task_assigned: 'タスクの担当になりました',
  task_unassigned: 'タスクの割り当てが解除されました',
  comment_added: '担当のタスクにコメントがありました',
  due_tomorrow: '明日が予定終了日のタスクがあります',
  overdue: '予定終了日を過ぎたタスクがあります',
  team_added: 'チームに追加されました',
};

export const AUTH_METHOD: Record<string, string> = {
  password: 'パスワードのみ',
  password_totp: 'パスワードと認証アプリ',
  password_recovery: 'パスワードとリカバリーコード',
  passkey: 'パスキー',
};

export const USER_STATUS: Record<string, string> = {
  invited: '招待中',
  active: '有効',
  disabled: '無効',
};

export type GroupBy = 'hierarchy' | 'assignee' | 'team' | 'status';
export type SortBy = 'manual' | 'planned_start' | 'planned_end' | 'priority' | 'progress';
export type Zoom = 'day' | 'week' | 'month' | 'quarter';
export type ColorBy = 'status' | 'assignee' | 'priority' | 'tag';
export type Column =
  | 'assignee'
  | 'status'
  | 'progress'
  | 'planned_dates'
  | 'actual_dates'
  | 'planned_minutes'
  | 'actual_minutes'
  | 'variance'
  | 'priority'
  | 'tags';

export const GROUP_LABEL: Record<GroupBy, string> = {
  hierarchy: '階層',
  assignee: '担当者別',
  team: 'チーム別',
  status: '状態別',
};

export const SORT_LABEL: Record<SortBy, string> = {
  manual: '階層と手動の並び順',
  planned_start: '予定開始日',
  planned_end: '予定終了日',
  priority: '優先度',
  progress: '進捗率',
};

export const ZOOM_LABEL: Record<Zoom, string> = { day: '日', week: '週', month: '月', quarter: '四半期' };

export const COLOR_LABEL: Record<ColorBy, string> = {
  status: '状態',
  assignee: '担当者',
  priority: '優先度',
  tag: 'タグ',
};

export const COLUMN_LABEL: Record<Column, string> = {
  assignee: '担当',
  status: '状態',
  progress: '進捗',
  planned_dates: '予定日程',
  actual_dates: '実績日程',
  planned_minutes: '予定工数',
  actual_minutes: '実績工数',
  variance: '工数差',
  priority: '優先度',
  tags: 'タグ',
};

export const ALL_COLUMNS = Object.keys(COLUMN_LABEL) as Column[];

export const RANGE_LABEL = {
  default: '標準（前 1 週〜後 5 週）',
  this_month: '今月',
  next_month: '来月',
  this_quarter: '今四半期',
  custom: '任意の期間',
} as const;
