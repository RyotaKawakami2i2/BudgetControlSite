/**
 * サーバーのデータの取得と更新（TanStack Query）。キャッシュのキーはここで決める。
 */
import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api, query } from './client';
import type {
  AdminTeam,
  AdminUser,
  AuditLogItem,
  Comment,
  CreateTaskInput,
  CursorPage,
  Dashboard,
  DeletedTask,
  Gantt,
  GanttTask,
  Guid,
  Holiday,
  Me,
  MyTasks,
  NotificationItem,
  SavedView,
  TaskDetail,
  TaskHistory,
  TaskOption,
  TeamDetail,
  TeamReport,
  TeamSummary,
  Timesheet,
  UpdateTaskInput,
  UserSearchResult,
  WorkLogList,
  WorkLogResult,
} from './types';

export const keys = {
  me: ['me'] as const,
  gantt: (teamIds: string[], from: string, to: string) => ['gantt', teamIds.join(','), from, to] as const,
  ganttSearch: (teamIds: string[], q: string) => ['gantt-search', teamIds.join(','), q] as const,
  task: (id: string) => ['task', id] as const,
  history: (id: string) => ['task-history', id] as const,
  comments: (id: string) => ['comments', id] as const,
  workLogs: (id: string) => ['work-logs', id] as const,
  teamTasks: (teamId: string) => ['team-tasks', teamId] as const,
  myTasks: ['my-tasks'] as const,
  dashboard: ['dashboard'] as const,
  timesheet: (weekStart: string) => ['timesheet', weekStart] as const,
  teams: (includeArchived: boolean, all: boolean) => ['teams', includeArchived, all] as const,
  team: (id: string) => ['team', id] as const,
  deletedTasks: (teamId: string) => ['deleted-tasks', teamId] as const,
  report: (teamId: string, from: string, to: string) => ['report', teamId, from, to] as const,
  views: ['views'] as const,
  notifications: ['notifications'] as const,
  unreadCount: ['unread-count'] as const,
  adminUsers: (q: string, status: string) => ['admin-users', q, status] as const,
  adminTeams: ['admin-teams'] as const,
  audit: (search: Record<string, string>) => ['audit', search] as const,
  holidays: (year: number) => ['holidays', year] as const,
};

/** タスクを変えたときに、表示を取り直すもの。 */
export function invalidateTaskData(client: QueryClient, taskId?: string): void {
  void client.invalidateQueries({ queryKey: ['gantt'] });
  void client.invalidateQueries({ queryKey: ['gantt-search'] });
  void client.invalidateQueries({ queryKey: keys.myTasks });
  void client.invalidateQueries({ queryKey: keys.dashboard });
  void client.invalidateQueries({ queryKey: ['timesheet'] });
  void client.invalidateQueries({ queryKey: ['team-tasks'] });
  void client.invalidateQueries({ queryKey: ['report'] });
  if (taskId) {
    void client.invalidateQueries({ queryKey: keys.task(taskId) });
    void client.invalidateQueries({ queryKey: keys.history(taskId) });
    void client.invalidateQueries({ queryKey: keys.workLogs(taskId) });
  } else {
    void client.invalidateQueries({ queryKey: ['task'] });
  }
}

// ---------------------------------------------------------------- 自分

export function useMe() {
  return useQuery({ queryKey: keys.me, queryFn: () => api.get<Me>('/me'), staleTime: 60_000 });
}

export function useUpdateMe() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (displayName: string) => api.patch<Me>('/me', { displayName }),
    onSuccess: (me) => client.setQueryData(keys.me, me),
  });
}

// ---------------------------------------------------------------- ガント

export function useGantt(teamIds: string[], from: string, to: string) {
  return useQuery({
    queryKey: keys.gantt(teamIds, from, to),
    queryFn: () => api.get<Gantt>(`/gantt${query({ teamIds, from, to })}`),
    enabled: teamIds.length > 0,
    placeholderData: keepPreviousData,
  });
}

export function useGanttSearch(teamIds: string[], q: string) {
  return useQuery({
    queryKey: keys.ganttSearch(teamIds, q),
    queryFn: () => api.get<{ taskIds: Guid[]; truncated: boolean }>(`/gantt/search${query({ teamIds, q })}`),
    enabled: teamIds.length > 0 && q.trim().length > 0,
    placeholderData: keepPreviousData,
  });
}

// ---------------------------------------------------------------- タスク

export function useTask(id: string | null) {
  return useQuery({
    queryKey: keys.task(id ?? ''),
    queryFn: () => api.get<TaskDetail>(`/tasks/${id}`),
    enabled: !!id,
  });
}

export function useTeamTasks(teamId: string | null) {
  return useQuery({
    queryKey: keys.teamTasks(teamId ?? ''),
    queryFn: () => api.get<TaskOption[]>(`/tasks${query({ teamId })}`),
    enabled: !!teamId,
  });
}

export function useCreateTask() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateTaskInput) => api.post<GanttTask>('/tasks', input),
    onSuccess: () => invalidateTaskData(client),
  });
}

export function useUpdateTask() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateTaskInput }) => api.patch<GanttTask>(`/tasks/${id}`, input),
    onSuccess: (_, { id }) => invalidateTaskData(client, id),
    onError: (_, { id }) => invalidateTaskData(client, id),
  });
}

export function useMoveTask() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, version, parentId, afterTaskId }: { id: string; version: number; parentId: string | null; afterTaskId: string | null }) =>
      api.post<GanttTask>(`/tasks/${id}/move`, { version, parentId, afterTaskId }),
    onSettled: (_, __, { id }) => invalidateTaskData(client, id),
  });
}

export function useDeleteTask() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, version }: { id: string; version: number }) => api.delete(`/tasks/${id}${query({ version })}`),
    onSuccess: () => invalidateTaskData(client),
  });
}

export function useDeletedTasks(teamId: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.deletedTasks(teamId),
    queryFn: () => api.get<DeletedTask[]>(`/teams/${teamId}/deleted-tasks`),
    enabled,
  });
}

export function useRestoreTask() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<GanttTask>(`/tasks/${id}/restore`),
    onSuccess: () => {
      invalidateTaskData(client);
      void client.invalidateQueries({ queryKey: ['deleted-tasks'] });
    },
  });
}

export function useHistory(id: string) {
  return useQuery({
    queryKey: keys.history(id),
    queryFn: () => api.get<CursorPage<TaskHistory>>(`/tasks/${id}/history${query({ limit: 100 })}`),
  });
}

export function useComments(id: string) {
  return useQuery({ queryKey: keys.comments(id), queryFn: () => api.get<Comment[]>(`/tasks/${id}/comments`) });
}

export function useCommentMutations(taskId: string) {
  const client = useQueryClient();
  const refresh = () => void client.invalidateQueries({ queryKey: keys.comments(taskId) });
  return {
    create: useMutation({ mutationFn: (body: string) => api.post<Comment>(`/tasks/${taskId}/comments`, { body }), onSuccess: refresh }),
    update: useMutation({
      mutationFn: ({ id, body }: { id: string; body: string }) => api.patch<Comment>(`/comments/${id}`, { body }),
      onSuccess: refresh,
    }),
    remove: useMutation({ mutationFn: (id: string) => api.delete(`/comments/${id}`), onSuccess: refresh }),
  };
}

export function useDependencyMutations(taskId: string) {
  const client = useQueryClient();
  return {
    add: useMutation({
      mutationFn: (predecessorId: string) => api.post<{ warnings: string[] }>(`/tasks/${taskId}/dependencies`, { predecessorId }),
      onSuccess: () => invalidateTaskData(client, taskId),
    }),
    remove: useMutation({
      mutationFn: (predecessorId: string) => api.delete(`/tasks/${taskId}/dependencies/${predecessorId}`),
      onSuccess: () => invalidateTaskData(client, taskId),
    }),
  };
}

// ---------------------------------------------------------------- 作業実績

export function useWorkLogs(taskId: string) {
  return useQuery({ queryKey: keys.workLogs(taskId), queryFn: () => api.get<WorkLogList>(`/tasks/${taskId}/work-logs`) });
}

export function useWorkLogMutations() {
  const client = useQueryClient();
  return {
    create: useMutation({
      mutationFn: ({ taskId, ...body }: { taskId: string; workDate: string; minutes: number; note: string | null; progress?: number | null; taskVersion?: number }) =>
        api.post<WorkLogResult>(`/tasks/${taskId}/work-logs`, body),
      onSuccess: (_, { taskId }) => invalidateTaskData(client, taskId),
    }),
    update: useMutation({
      mutationFn: ({ id, ...body }: { id: string; taskId: string; version: number; workDate: string; minutes: number; note: string | null }) =>
        api.patch<WorkLogResult>(`/work-logs/${id}`, body),
      onSuccess: (_, { taskId }) => invalidateTaskData(client, taskId),
    }),
    remove: useMutation({
      mutationFn: ({ id }: { id: string; taskId: string }) => api.delete(`/work-logs/${id}`),
      onSuccess: (_, { taskId }) => invalidateTaskData(client, taskId),
    }),
  };
}

export function useTimesheet(weekStart: string) {
  return useQuery({ queryKey: keys.timesheet(weekStart), queryFn: () => api.get<Timesheet>(`/me/timesheet${query({ weekStart })}`) });
}

export function useSaveTimesheet() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: { weekStart: string; cells: Array<{ taskId: string; date: string; minutes: number }> }) =>
      api.put<Timesheet>('/me/timesheet', body),
    onSuccess: (data) => {
      client.setQueryData(keys.timesheet(data.weekStart), data);
      invalidateTaskData(client);
    },
  });
}

// ---------------------------------------------------------------- ホーム・マイタスク・予実

export function useMyTasks() {
  return useQuery({ queryKey: keys.myTasks, queryFn: () => api.get<MyTasks>('/me/tasks') });
}

export function useDashboard() {
  return useQuery({ queryKey: keys.dashboard, queryFn: () => api.get<Dashboard>('/dashboard') });
}

export function useTeamReport(teamId: string, from: string, to: string) {
  return useQuery({
    queryKey: keys.report(teamId, from, to),
    queryFn: () => api.get<TeamReport>(`/teams/${teamId}/report${query({ from, to })}`),
    placeholderData: keepPreviousData,
  });
}

// ---------------------------------------------------------------- チーム

export function useTeams(includeArchived = false, all = false) {
  return useQuery({
    queryKey: keys.teams(includeArchived, all),
    queryFn: () => api.get<TeamSummary[]>(`/teams${query({ includeArchived, scope: all ? 'all' : null })}`),
  });
}

export function useTeam(id: string | null) {
  return useQuery({ queryKey: keys.team(id ?? ''), queryFn: () => api.get<TeamDetail>(`/teams/${id}`), enabled: !!id });
}

export function useTeamMutations() {
  const client = useQueryClient();
  const refresh = (team: TeamDetail) => {
    client.setQueryData(keys.team(team.id), team);
    void client.invalidateQueries({ queryKey: ['teams'] });
    void client.invalidateQueries({ queryKey: keys.me });
    void client.invalidateQueries({ queryKey: keys.adminTeams });
    void client.invalidateQueries({ queryKey: ['gantt'] });
  };
  return {
    create: useMutation({
      mutationFn: (body: { name: string; description: string | null; leaderUserId: string | null }) => api.post<TeamDetail>('/teams', body),
      onSuccess: refresh,
    }),
    update: useMutation({
      mutationFn: ({ id, ...body }: { id: string; version: number; name?: string; description?: string | null }) =>
        api.patch<TeamDetail>(`/teams/${id}`, body),
      onSuccess: refresh,
    }),
    archive: useMutation({
      mutationFn: ({ id, archived, version }: { id: string; archived: boolean; version: number }) =>
        api.post<TeamDetail>(`/teams/${id}/${archived ? 'archive' : 'unarchive'}`, { version }),
      onSuccess: refresh,
    }),
    addMember: useMutation({
      mutationFn: ({ id, userId, role }: { id: string; userId: string; role: string }) =>
        api.post<TeamDetail>(`/teams/${id}/members`, { userId, role }),
      onSuccess: refresh,
    }),
    changeRole: useMutation({
      mutationFn: ({ id, userId, role }: { id: string; userId: string; role: string }) =>
        api.patch<TeamDetail>(`/teams/${id}/members/${userId}`, { role }),
      onSuccess: refresh,
    }),
    removeMember: useMutation({
      mutationFn: ({ id, userId }: { id: string; userId: string }) => api.delete<TeamDetail>(`/teams/${id}/members/${userId}`),
      onSuccess: (team) => {
        refresh(team);
        invalidateTaskData(client);
      },
    }),
    createTag: useMutation({
      mutationFn: ({ teamId, name, color }: { teamId: string; name: string; color: string }) => api.post(`/teams/${teamId}/tags`, { name, color }),
      onSuccess: () => {
        void client.invalidateQueries({ queryKey: ['team'] });
        void client.invalidateQueries({ queryKey: ['gantt'] });
      },
    }),
    updateTag: useMutation({
      mutationFn: ({ id, name, color }: { id: string; name: string; color: string }) => api.patch(`/tags/${id}`, { name, color }),
      onSuccess: () => {
        void client.invalidateQueries({ queryKey: ['team'] });
        void client.invalidateQueries({ queryKey: ['gantt'] });
      },
    }),
    deleteTag: useMutation({
      mutationFn: (id: string) => api.delete(`/tags/${id}`),
      onSuccess: () => {
        void client.invalidateQueries({ queryKey: ['team'] });
        void client.invalidateQueries({ queryKey: ['gantt'] });
      },
    }),
  };
}

export function useUserSearch(q: string) {
  return useQuery({
    queryKey: ['user-search', q],
    queryFn: () => api.get<UserSearchResult[]>(`/users/search${query({ q })}`),
    enabled: q.trim().length >= 2,
  });
}

// ---------------------------------------------------------------- ビュー・通知

export function useViews() {
  return useQuery({ queryKey: keys.views, queryFn: () => api.get<SavedView[]>('/views') });
}

export function useViewMutations() {
  const client = useQueryClient();
  const refresh = () => {
    void client.invalidateQueries({ queryKey: keys.views });
    void client.invalidateQueries({ queryKey: keys.me });
  };
  return {
    create: useMutation({
      mutationFn: (body: { name: string; conditions: Record<string, unknown>; isShared: boolean; teamId: string | null }) =>
        api.post<SavedView>('/views', body),
      onSuccess: refresh,
    }),
    update: useMutation({
      mutationFn: ({ id, ...body }: { id: string; version: number; name?: string; conditions?: Record<string, unknown> }) =>
        api.patch<SavedView>(`/views/${id}`, body),
      onSuccess: refresh,
    }),
    remove: useMutation({ mutationFn: (id: string) => api.delete(`/views/${id}`), onSuccess: refresh }),
    setDefault: useMutation({ mutationFn: (viewId: string | null) => api.put('/me/default-view', { viewId }), onSuccess: refresh }),
  };
}

export function useNotifications() {
  return useQuery({ queryKey: keys.notifications, queryFn: () => api.get<CursorPage<NotificationItem>>(`/notifications${query({ limit: 100 })}`) });
}

export function useUnreadCount() {
  return useQuery({
    queryKey: keys.unreadCount,
    queryFn: () => api.get<{ count: number }>('/notifications/unread-count'),
    refetchInterval: 60_000,
  });
}

export function useNotificationMutations() {
  const client = useQueryClient();
  const refresh = () => {
    void client.invalidateQueries({ queryKey: keys.notifications });
    void client.invalidateQueries({ queryKey: keys.unreadCount });
  };
  return {
    read: useMutation({ mutationFn: (id: string) => api.post(`/notifications/${id}/read`), onSuccess: refresh }),
    readAll: useMutation({ mutationFn: () => api.post('/notifications/read-all'), onSuccess: refresh }),
  };
}

// ---------------------------------------------------------------- 管理

export function useAdminUsers(q: string, status: string) {
  return useQuery({
    queryKey: keys.adminUsers(q, status),
    queryFn: () => api.get<AdminUser[]>(`/admin/users${query({ q, status })}`),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useAdminUserMutations() {
  const client = useQueryClient();
  const refresh = () => void client.invalidateQueries({ queryKey: ['admin-users'] });
  return {
    invite: useMutation({
      mutationFn: (body: { email: string; displayName: string; isAdmin: boolean }) => api.post<AdminUser>('/admin/invitations', body),
      onSuccess: refresh,
    }),
    resend: useMutation({ mutationFn: (id: string) => api.post<AdminUser>(`/admin/invitations/${id}/resend`), onSuccess: refresh }),
    revoke: useMutation({ mutationFn: (id: string) => api.delete<AdminUser>(`/admin/invitations/${id}`), onSuccess: refresh }),
    disable: useMutation({ mutationFn: (id: string) => api.post<AdminUser>(`/admin/users/${id}/disable`), onSuccess: refresh }),
    enable: useMutation({ mutationFn: (id: string) => api.post<AdminUser>(`/admin/users/${id}/enable`), onSuccess: refresh }),
    setAdmin: useMutation({
      mutationFn: ({ id, isAdmin }: { id: string; isAdmin: boolean }) => api.put<AdminUser>(`/admin/users/${id}/admin`, { isAdmin }),
      onSuccess: refresh,
    }),
    resetMfa: useMutation({
      mutationFn: ({ id, verification }: { id: string; verification: string }) =>
        api.post<AdminUser>(`/admin/users/${id}/reset-mfa`, { verification }),
      onSuccess: refresh,
    }),
  };
}

export function useAdminTeams() {
  return useQuery({ queryKey: keys.adminTeams, queryFn: () => api.get<AdminTeam[]>('/admin/teams'), retry: false });
}

export function useAuditLogs(search: Record<string, string>, cursor: string | null) {
  return useQuery({
    queryKey: [...keys.audit(search), cursor],
    queryFn: () => api.get<CursorPage<AuditLogItem>>(`/admin/audit-logs${query({ ...search, cursor, limit: 50 })}`),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useHolidays(year: number) {
  return useQuery({ queryKey: keys.holidays(year), queryFn: () => api.get<Holiday[]>(`/admin/holidays${query({ year })}`), retry: false });
}

export function useHolidayMutations() {
  const client = useQueryClient();
  const refresh = () => {
    void client.invalidateQueries({ queryKey: ['holidays'] });
    void client.invalidateQueries({ queryKey: ['gantt'] });
  };
  return {
    add: useMutation({ mutationFn: (body: { date: string; name: string }) => api.post<Holiday>('/admin/holidays', body), onSuccess: refresh }),
    remove: useMutation({ mutationFn: (date: string) => api.delete(`/admin/holidays/${date}`), onSuccess: refresh }),
  };
}
