/**
 * API の入出力の型（詳細設計書 5章）。サーバーの入出力の型（Application 層の record）に合わせる。
 * 項目名は camelCase、区分値は snake_case のコード、日付は "YYYY-MM-DD"、日時は ISO 8601 の UTC。
 */

export type Guid = string;
export type IsoDate = string;
export type IsoDateTime = string;

export type TaskStatus = 'not_started' | 'in_progress' | 'on_hold' | 'done' | 'cancelled';
export type Priority = 'high' | 'medium' | 'low';
export type TeamRole = 'leader' | 'member';
export type TeamRoleCode = TeamRole | 'admin_view';
export type DelayFlag = 'overdue' | 'late_start' | 'effort_overrun' | 'progress_lag';
export type TagColor = 'gray' | 'blue' | 'green' | 'yellow' | 'orange' | 'red' | 'purple' | 'teal';
export type NotificationKind = 'task_assigned' | 'task_unassigned' | 'comment_added' | 'due_tomorrow' | 'overdue' | 'team_added';
export type UserStatus = 'invited' | 'active' | 'disabled';
export type WorkLogSource = 'dialog' | 'timesheet';
export type HistoryKind =
  | 'created'
  | 'updated'
  | 'moved'
  | 'deleted'
  | 'restored'
  | 'worklog_added'
  | 'worklog_updated'
  | 'worklog_deleted'
  | 'dependency_added'
  | 'dependency_removed';

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
}

// ---------------------------------------------------------------- 自分

export interface MyTeam {
  id: Guid;
  name: string;
  role: TeamRole;
  archived: boolean;
}

export interface Me {
  id: Guid;
  displayName: string;
  email: string;
  isAdmin: boolean;
  canCreateTeam: boolean;
  mfaEnabled: boolean;
  hasAuthenticator: boolean;
  passkeyCount: number;
  teams: MyTeam[];
  defaultViewId: Guid | null;
}

export interface SessionStatus {
  remainingSeconds: number;
  absoluteRemainingSeconds: number;
  warningSeconds: number;
  authMethod: string;
}

// ---------------------------------------------------------------- タスク

export interface TaskCan {
  editPlan: boolean;
  assign: boolean;
  editActual: boolean;
  logWork: boolean;
  addChild: boolean;
  delete: boolean;
}

export interface GanttTask {
  id: Guid;
  teamId: Guid;
  parentId: Guid | null;
  depth: number;
  sortOrder: number;
  title: string;
  assigneeId: Guid | null;
  createdById: Guid;
  status: TaskStatus;
  priority: Priority;
  isMilestone: boolean;
  isSummary: boolean;
  plannedStart: IsoDate | null;
  plannedEnd: IsoDate | null;
  plannedMinutes: number | null;
  actualStart: IsoDate | null;
  actualEnd: IsoDate | null;
  actualMinutes: number;
  progress: number;
  expectedProgress: number | null;
  flags: DelayFlag[];
  descendantFlagged: boolean;
  tagIds: Guid[];
  predecessorIds: Guid[];
  version: number;
  can: TaskCan;
}

export interface TaskRef {
  id: Guid;
  title: string;
}

export interface Tag {
  id: Guid;
  teamId: Guid;
  name: string;
  color: TagColor;
}

export interface Dependency {
  id: Guid;
  title: string;
  plannedStart: IsoDate | null;
  plannedEnd: IsoDate | null;
  status: TaskStatus;
}

export interface TaskDetailCan extends TaskCan {
  move: boolean;
  editDependencies: boolean;
  comment: boolean;
  viewAllWorkLogs: boolean;
  nextStatuses: TaskStatus[];
}

export interface TaskDetail {
  task: GanttTask;
  description: string | null;
  resultNote: string | null;
  teamName: string;
  teamArchived: boolean;
  role: TeamRoleCode;
  path: TaskRef[];
  assigneeName: string | null;
  createdByName: string;
  tags: Tag[];
  predecessors: Dependency[];
  successors: Dependency[];
  childCount: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  can: TaskDetailCan;
  warnings: string[];
}

export interface TaskOption {
  id: Guid;
  parentId: Guid | null;
  depth: number;
  title: string;
  isMilestone: boolean;
  status: TaskStatus;
  hasChildren: boolean;
  assigneeId: Guid | null;
  createdById: Guid;
}

export interface CreateTaskInput {
  teamId: Guid;
  parentId: Guid | null;
  title: string;
  description: string | null;
  assigneeId: Guid | null;
  plannedStart: IsoDate | null;
  plannedEnd: IsoDate | null;
  plannedMinutes: number | null;
  priority: Priority;
  tagIds: Guid[];
  isMilestone: boolean;
}

/** 変更の要求。送った項目だけが変わる（null は「空にする」）。 */
export interface UpdateTaskInput {
  version: number;
  title?: string;
  description?: string | null;
  assigneeId?: Guid | null;
  plannedStart?: IsoDate | null;
  plannedEnd?: IsoDate | null;
  plannedMinutes?: number | null;
  priority?: Priority;
  tagIds?: Guid[];
  isMilestone?: boolean;
  status?: TaskStatus;
  progress?: number;
  actualStart?: IsoDate | null;
  actualEnd?: IsoDate | null;
  resultNote?: string | null;
}

export interface DeletedTask {
  id: Guid;
  title: string;
  parentId: Guid | null;
  deletedAt: IsoDateTime;
  deletedByName: string | null;
  descendantCount: number;
  canRestore: boolean;
}

export interface TaskHistory {
  id: number;
  occurredAt: IsoDateTime;
  actorId: Guid;
  actorName: string;
  kind: HistoryKind;
  field: string | null;
  oldValue: string | null;
  newValue: string | null;
}

export interface Comment {
  id: Guid;
  taskId: Guid;
  authorId: Guid;
  authorName: string;
  body: string | null;
  createdAt: IsoDateTime;
  editedAt: IsoDateTime | null;
  deleted: boolean;
  canEdit: boolean;
}

// ---------------------------------------------------------------- 作業実績

export interface WorkLog {
  id: Guid;
  taskId: Guid;
  taskTitle: string;
  userId: Guid;
  userName: string;
  workDate: IsoDate;
  minutes: number;
  note: string | null;
  source: WorkLogSource;
  version: number;
  createdAt: IsoDateTime;
  updatedAt: IsoDateTime;
  canEdit: boolean;
}

export interface WorkLogList {
  mine: WorkLog[];
  totals: Array<{ userId: Guid; userName: string; minutes: number }>;
  all: WorkLog[] | null;
  totalMinutes: number;
}

export interface WorkLogResult {
  workLog: WorkLog;
  task: { id: Guid; status: TaskStatus; progress: number; actualStart: IsoDate | null; actualMinutes: number; version: number };
}

export interface Timesheet {
  weekStart: IsoDate;
  days: IsoDate[];
  rows: Array<{
    taskId: Guid;
    title: string;
    teamId: Guid;
    teamName: string;
    status: TaskStatus;
    editable: boolean;
    cells: Array<{ date: IsoDate; minutes: number; locked: boolean }>;
  }>;
  dayTotals: number[];
  addable: Array<{ taskId: Guid; title: string; teamName: string; status: TaskStatus }>;
}

// ---------------------------------------------------------------- ガント

export interface GanttTeam {
  id: Guid;
  name: string;
  role: TeamRoleCode;
  archived: boolean;
}

export interface GanttMember {
  id: Guid;
  displayName: string;
  teamIds: Guid[];
  current: boolean;
  disabled: boolean;
}

export interface Holiday {
  date: IsoDate;
  name: string;
}

export interface Gantt {
  asOf: IsoDate;
  range: { from: IsoDate; to: IsoDate };
  teams: GanttTeam[];
  members: GanttMember[];
  tags: Tag[];
  holidays: Holiday[];
  tasks: GanttTask[];
  truncated: boolean;
}

// ---------------------------------------------------------------- ホーム・マイタスク・予実

export interface MyTask {
  id: Guid;
  teamId: Guid;
  teamName: string;
  title: string;
  path: TaskRef[];
  status: TaskStatus;
  priority: Priority;
  plannedStart: IsoDate | null;
  plannedEnd: IsoDate | null;
  plannedMinutes: number | null;
  actualStart: IsoDate | null;
  actualMinutes: number;
  progress: number;
  expectedProgress: number | null;
  flags: DelayFlag[];
  version: number;
  can: TaskCan;
  nextStatuses: TaskStatus[];
}

export interface MyTasks {
  today: IsoDate;
  overdue: MyTask[];
  dueToday: MyTask[];
  dueThisWeek: MyTask[];
  later: MyTask[];
  unscheduled: MyTask[];
}

export interface Dashboard {
  today: IsoDate;
  overdue: { count: number; items: MyTask[] };
  dueToday: { count: number; items: MyTask[] };
  dueThisWeek: { count: number; items: MyTask[] };
  teams: Array<{
    teamId: Guid;
    name: string;
    role: TeamRole;
    taskCount: number;
    doneCount: number;
    completionRate: number;
    delayedCount: number;
    plannedMinutesToDate: number;
    actualMinutesToDate: number;
    start: IsoDate | null;
    end: IsoDate | null;
    progress: number;
  }>;
}

export interface TeamReportRow {
  userId: Guid | null;
  displayName: string;
  currentMember: boolean;
  taskCount: number;
  plannedMinutes: number;
  actualMinutes: number;
  varianceMinutes: number;
  delayedCount: number;
}

export interface TeamReport {
  teamId: Guid;
  teamName: string;
  range: { from: IsoDate; to: IsoDate };
  rows: TeamReportRow[];
  total: TeamReportRow;
}

// ---------------------------------------------------------------- チーム

export interface TeamSummary {
  id: Guid;
  name: string;
  description: string | null;
  archived: boolean;
  role: TeamRoleCode;
  memberCount: number;
  version: number;
}

export interface TeamMember {
  userId: Guid;
  displayName: string;
  email: string | null;
  role: TeamRole;
  joinedAt: IsoDateTime;
  disabled: boolean;
}

export interface TeamDetail {
  id: Guid;
  name: string;
  description: string | null;
  archived: boolean;
  archivedAt: IsoDateTime | null;
  version: number;
  role: TeamRoleCode;
  members: TeamMember[];
  tags: Tag[];
  can: {
    update: boolean;
    archive: boolean;
    unarchive: boolean;
    manageMembers: boolean;
    manageTags: boolean;
    manageSharedViews: boolean;
    viewReport: boolean;
    createTask: boolean;
    restore: boolean;
  };
}

export interface UserSearchResult {
  id: Guid;
  displayName: string;
  email: string;
}

// ---------------------------------------------------------------- ビュー・通知

export interface SavedView {
  id: Guid;
  name: string;
  isShared: boolean;
  teamId: Guid | null;
  teamName: string | null;
  ownerId: Guid;
  ownerName: string;
  conditions: Record<string, unknown>;
  isDefault: boolean;
  canEdit: boolean;
  version: number;
}

export interface NotificationItem {
  id: Guid;
  kind: NotificationKind;
  teamId: Guid | null;
  teamName: string | null;
  taskId: Guid | null;
  taskTitle: string | null;
  taskVisible: boolean;
  actorName: string | null;
  createdAt: IsoDateTime;
  readAt: IsoDateTime | null;
}

// ---------------------------------------------------------------- 管理

export interface AdminUser {
  id: Guid;
  email: string;
  displayName: string;
  status: UserStatus;
  isAdmin: boolean;
  mfaEnabled: boolean;
  passkeyCount: number;
  teamCount: number;
  lastLoginAt: IsoDateTime | null;
  createdAt: IsoDateTime;
  invitationExpiresAt: IsoDateTime | null;
  locked: boolean;
}

export interface AdminTeam {
  id: Guid;
  name: string;
  description: string | null;
  archived: boolean;
  memberCount: number;
  leaders: string[];
  createdAt: IsoDateTime;
}

export interface AuditLogItem {
  id: number;
  occurredAt: IsoDateTime;
  actorId: Guid | null;
  actorName: string | null;
  action: string;
  result: 'success' | 'failure' | 'denied';
  targetType: string | null;
  targetId: string | null;
  teamId: Guid | null;
  ip: string | null;
  userAgent: string | null;
  requestId: string | null;
  detail: unknown;
}
