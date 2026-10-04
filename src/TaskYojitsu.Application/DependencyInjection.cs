using Microsoft.Extensions.DependencyInjection;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Application.Admin;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Gantt;
using TaskYojitsu.Application.Home;
using TaskYojitsu.Application.Me;
using TaskYojitsu.Application.Notifications;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Application.Teams;
using TaskYojitsu.Application.Views;
using TaskYojitsu.Application.WorkLogs;

namespace TaskYojitsu.Application;

public static class DependencyInjection
{
    /// <summary>利用場面ごとの処理を登録する。要求ごとに作る（判定の材料は要求ごとに DB から読む）。</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<BusinessClock>();

        services.AddScoped<AccessPolicy>();
        services.AddScoped<TaskDataLoader>();
        services.AddScoped<TaskHistoryWriter>();
        services.AddScoped<Notifier>();

        services.AddScoped<TeamService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<TagService>();
        services.AddScoped<TaskService>();
        services.AddScoped<TaskQueries>();
        services.AddScoped<CommentService>();
        services.AddScoped<DependencyService>();
        services.AddScoped<WorkLogService>();
        services.AddScoped<TimesheetService>();
        services.AddScoped<GanttQuery>();
        services.AddScoped<MyTasksQuery>();
        services.AddScoped<DashboardQuery>();
        services.AddScoped<TeamReportQuery>();
        services.AddScoped<ViewService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<MeService>();

        services.AddScoped<SessionRevoker>();
        services.AddScoped<Mailer>();
        services.AddScoped<InvitationService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<AdminTeamQuery>();
        services.AddScoped<AuditLogQuery>();
        services.AddScoped<HolidayService>();
        return services;
    }
}
