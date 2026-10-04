import { createBrowserRouter, Navigate } from 'react-router';
import { AppLayout } from './layout/AppLayout';
import { AdminTeamsPage } from './features/admin/AdminTeamsPage';
import { AdminUsersPage } from './features/admin/AdminUsersPage';
import { AuditLogsPage } from './features/admin/AuditLogsPage';
import { HolidaysPage } from './features/admin/HolidaysPage';
import { GanttPage } from './features/gantt/GanttPage';
import { HomePage } from './features/home/HomePage';
import { MyTasksPage } from './features/my-tasks/MyTasksPage';
import { NotificationsPage } from './features/notifications/NotificationsPage';
import { SettingsPage } from './features/settings/SettingsPage';
import { TaskListPage } from './features/task-list/TaskListPage';
import { TeamDetailPage } from './features/teams/TeamDetailPage';
import { TeamReportPage } from './features/teams/TeamReportPage';
import { TeamsPage } from './features/teams/TeamsPage';
import { TimesheetPage } from './features/timesheet/TimesheetPage';
import { NotFoundPage } from './layout/NotFoundPage';

/** 業務の画面のルーティング（基本設計書 4.1）。URL は /app 以下。 */
export const router = createBrowserRouter(
  [
    {
      path: '/',
      element: <AppLayout />,
      children: [
        { index: true, element: <HomePage /> },
        { path: 'gantt', element: <GanttPage /> },
        { path: 'my-tasks', element: <MyTasksPage /> },
        { path: 'timesheet', element: <TimesheetPage /> },
        { path: 'tasks', element: <TaskListPage /> },
        { path: 'teams', element: <TeamsPage /> },
        { path: 'teams/:teamId', element: <TeamDetailPage /> },
        { path: 'teams/:teamId/report', element: <TeamReportPage /> },
        { path: 'notifications', element: <NotificationsPage /> },
        { path: 'settings', element: <SettingsPage /> },
        { path: 'admin', element: <Navigate to="/admin/users" replace /> },
        { path: 'admin/users', element: <AdminUsersPage /> },
        { path: 'admin/teams', element: <AdminTeamsPage /> },
        { path: 'admin/audit-logs', element: <AuditLogsPage /> },
        { path: 'admin/holidays', element: <HolidaysPage /> },
        { path: '*', element: <NotFoundPage /> },
      ],
    },
  ],
  { basename: '/app' },
);
