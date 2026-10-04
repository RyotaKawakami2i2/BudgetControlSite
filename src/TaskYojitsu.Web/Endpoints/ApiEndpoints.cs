using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Admin;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Gantt;
using TaskYojitsu.Application.Home;
using TaskYojitsu.Application.Me;
using TaskYojitsu.Application.Notifications;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Application.Teams;
using TaskYojitsu.Application.Views;
using TaskYojitsu.Application.WorkLogs;
using TaskYojitsu.Infrastructure.Sessions;
using TaskYojitsu.Web.Security;

namespace TaskYojitsu.Web.Endpoints;

/// <summary>
/// API（詳細設計書 5.3）。すべての API で認証を必須とし、状態を変える要求では CSRF などを確かめる。
/// 権限の判定は Application 層の AccessPolicy で行い、ここには条件を書かない（NF-ACC-05）。
/// </summary>
public static class ApiEndpoints
{
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .AddEndpointFilter<AppExceptionFilter>()
            .AddEndpointFilter<ApiRequestGuard>();

        MapMe(api);
        MapTeams(api);
        MapTasks(api);
        MapWorkLogs(api);
        MapGantt(api);
        MapViewsAndNotifications(api);
        MapAdmin(api.MapGroup("/admin").AddEndpointFilter<AdminAccessFilter>());

        // 定義されていない API は 404（Problem Details）
        api.Map("/{**rest}", (HttpContext http) =>
            Problems.Create(http, StatusCodes.Status404NotFound, "resource.not_found", "対象が見つかりません。"));
    }

    private static void MapMe(RouteGroupBuilder api)
    {
        // API-01、02
        api.MapGet("/me", async (MeService me, HttpContext http, CancellationToken ct) =>
        {
            // 開発時に画面を Vite から配信している場合のため、CSRF のトークンもここで渡す
            XsrfCookie.Issue(http);
            return TypedResults.Ok(await me.GetAsync(ct));
        });
        api.MapPatch("/me", async (UpdateMeRequest body, MeService me, CancellationToken ct) => TypedResults.Ok(await me.UpdateAsync(body, ct)));

        // API-03
        api.MapPut("/me/default-view", async (DefaultViewRequest body, ViewService views, CancellationToken ct) =>
        {
            await views.SetDefaultAsync(body, ct);
            return TypedResults.NoContent();
        });

        // API-04、05（状態の確認では延長しない）
        api.MapGet("/session/status", (HttpContext http, IOptions<SecurityOptions> options, TimeProvider time) =>
            TypedResults.Ok(SessionStatus(http, options.Value, time)));
        api.MapPost("/session/keepalive", (HttpContext http, IOptions<SecurityOptions> options, TimeProvider time) =>
            TypedResults.Ok(SessionStatus(http, options.Value, time)));

        // API-42、43
        api.MapGet("/me/tasks", async (MyTasksQuery query, CancellationToken ct) => TypedResults.Ok(await query.GetAsync(ct)));
        api.MapGet("/dashboard", async (DashboardQuery query, CancellationToken ct) => TypedResults.Ok(await query.GetAsync(ct)));

        // API-40、41
        api.MapGet("/me/timesheet", async (DateOnly? weekStart, TimesheetService timesheet, BusinessClock clock, CancellationToken ct) =>
            TypedResults.Ok(await timesheet.GetAsync(weekStart ?? BusinessClock.MondayOf(clock.Today), ct)));
        api.MapPut("/me/timesheet", async (SaveTimesheetRequest body, TimesheetService timesheet, CancellationToken ct) =>
            TypedResults.Ok(await timesheet.SaveAsync(body, ct)));
    }

    private static void MapTeams(RouteGroupBuilder api)
    {
        // API-06〜11
        api.MapGet("/teams", async (bool? includeArchived, string? scope, TeamService teams, CancellationToken ct) =>
            TypedResults.Ok(await teams.ListAsync(includeArchived ?? false, scope == "all", ct)));
        api.MapPost("/teams", async (CreateTeamRequest body, TeamService teams, CancellationToken ct) =>
        {
            var team = await teams.CreateAsync(body, ct);
            return TypedResults.Created($"/api/v1/teams/{team.Id}", team);
        });
        api.MapGet("/teams/{teamId:guid}", async (Guid teamId, TeamService teams, CancellationToken ct) =>
            TypedResults.Ok(await teams.GetAsync(teamId, ct)));
        api.MapPatch("/teams/{teamId:guid}", async (Guid teamId, UpdateTeamRequest body, TeamService teams, CancellationToken ct) =>
            TypedResults.Ok(await teams.UpdateAsync(teamId, body, ct)));
        api.MapPost("/teams/{teamId:guid}/archive", async (Guid teamId, [FromBody] VersionRequest? body, TeamService teams, CancellationToken ct) =>
            TypedResults.Ok(await teams.SetArchivedAsync(teamId, true, body?.Version, ct)));
        api.MapPost("/teams/{teamId:guid}/unarchive", async (Guid teamId, [FromBody] VersionRequest? body, TeamService teams, CancellationToken ct) =>
            TypedResults.Ok(await teams.SetArchivedAsync(teamId, false, body?.Version, ct)));

        // API-12〜14
        api.MapPost("/teams/{teamId:guid}/members", async (Guid teamId, AddMemberRequest body, MembershipService members, CancellationToken ct) =>
            TypedResults.Ok(await members.AddAsync(teamId, body, ct)));
        api.MapPatch("/teams/{teamId:guid}/members/{userId:guid}", async (Guid teamId, Guid userId, ChangeRoleRequest body, MembershipService members, CancellationToken ct) =>
            TypedResults.Ok(await members.ChangeRoleAsync(teamId, userId, body, ct)));
        api.MapDelete("/teams/{teamId:guid}/members/{userId:guid}", async (Guid teamId, Guid userId, MembershipService members, CancellationToken ct) =>
            TypedResults.Ok(await members.RemoveAsync(teamId, userId, ct)));

        // API-15
        api.MapGet("/users/search", async (string? q, TeamService teams, CancellationToken ct) =>
            TypedResults.Ok(await teams.SearchUsersAsync(q, ct)));

        // API-16〜18
        api.MapPost("/teams/{teamId:guid}/tags", async (Guid teamId, TagRequest body, TagService tags, CancellationToken ct) =>
            TypedResults.Ok(await tags.CreateAsync(teamId, body, ct)));
        api.MapPatch("/tags/{tagId:guid}", async (Guid tagId, TagRequest body, TagService tags, CancellationToken ct) =>
            TypedResults.Ok(await tags.UpdateAsync(tagId, body, ct)));
        api.MapDelete("/tags/{tagId:guid}", async (Guid tagId, TagService tags, CancellationToken ct) =>
        {
            await tags.DeleteAsync(tagId, ct);
            return TypedResults.NoContent();
        });

        // API-26、44
        api.MapGet("/teams/{teamId:guid}/deleted-tasks", async (Guid teamId, TaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.ListDeletedAsync(teamId, ct)));
        api.MapGet("/teams/{teamId:guid}/report", async (Guid teamId, DateOnly? from, DateOnly? to, TeamReportQuery report, BusinessClock clock, CancellationToken ct) =>
        {
            var today = clock.Today;
            var start = from ?? new DateOnly(today.Year, today.Month, 1);
            var end = to ?? start.AddMonths(1).AddDays(-1);
            return TypedResults.Ok(await report.GetAsync(teamId, start, end, ct));
        });
    }

    private static void MapTasks(RouteGroupBuilder api)
    {
        // API-35（簡易版。チームのタスクの選択肢）
        api.MapGet("/tasks", async (Guid teamId, TaskQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.ListOptionsAsync(teamId, ct)));

        // API-21〜28
        api.MapGet("/tasks/{taskId:guid}", async (Guid taskId, TaskQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.GetDetailAsync(taskId, ct)));
        api.MapPost("/tasks", async (CreateTaskRequest body, TaskService tasks, CancellationToken ct) =>
        {
            var task = await tasks.CreateAsync(body, ct);
            return TypedResults.Created($"/api/v1/tasks/{task.Id}", task);
        });
        api.MapPatch("/tasks/{taskId:guid}", async (Guid taskId, UpdateTaskRequest body, TaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.UpdateAsync(taskId, body, ct)));
        api.MapPost("/tasks/{taskId:guid}/move", async (Guid taskId, MoveTaskRequest body, TaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.MoveAsync(taskId, body, ct)));
        api.MapDelete("/tasks/{taskId:guid}", async (Guid taskId, int version, TaskService tasks, CancellationToken ct) =>
        {
            await tasks.DeleteAsync(taskId, version, ct);
            return TypedResults.NoContent();
        });
        api.MapPost("/tasks/{taskId:guid}/restore", async (Guid taskId, TaskService tasks, CancellationToken ct) =>
            TypedResults.Ok(await tasks.RestoreAsync(taskId, ct)));
        api.MapGet("/tasks/{taskId:guid}/history", async (Guid taskId, string? cursor, int? limit, TaskQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.GetHistoryAsync(taskId, cursor, limit, ct)));

        // API-29〜32
        api.MapGet("/tasks/{taskId:guid}/comments", async (Guid taskId, CommentService comments, CancellationToken ct) =>
            TypedResults.Ok(await comments.ListAsync(taskId, ct)));
        api.MapPost("/tasks/{taskId:guid}/comments", async (Guid taskId, CommentRequest body, CommentService comments, CancellationToken ct) =>
            TypedResults.Ok(await comments.CreateAsync(taskId, body, ct)));
        api.MapPatch("/comments/{commentId:guid}", async (Guid commentId, CommentRequest body, CommentService comments, CancellationToken ct) =>
            TypedResults.Ok(await comments.UpdateAsync(commentId, body, ct)));
        api.MapDelete("/comments/{commentId:guid}", async (Guid commentId, CommentService comments, CancellationToken ct) =>
        {
            await comments.DeleteAsync(commentId, ct);
            return TypedResults.NoContent();
        });

        // API-33、34
        api.MapPost("/tasks/{taskId:guid}/dependencies", async (Guid taskId, AddDependencyRequest body, DependencyService deps, CancellationToken ct) =>
            TypedResults.Ok(await deps.AddAsync(taskId, body, ct)));
        api.MapDelete("/tasks/{taskId:guid}/dependencies/{predecessorId:guid}", async (Guid taskId, Guid predecessorId, DependencyService deps, CancellationToken ct) =>
        {
            await deps.RemoveAsync(taskId, predecessorId, ct);
            return TypedResults.NoContent();
        });
    }

    private static void MapWorkLogs(RouteGroupBuilder api)
    {
        // API-36〜39
        api.MapGet("/tasks/{taskId:guid}/work-logs", async (Guid taskId, WorkLogService logs, CancellationToken ct) =>
            TypedResults.Ok(await logs.ListAsync(taskId, ct)));
        api.MapPost("/tasks/{taskId:guid}/work-logs", async (Guid taskId, CreateWorkLogRequest body, WorkLogService logs, CancellationToken ct) =>
        {
            var result = await logs.CreateAsync(taskId, body, ct);
            return TypedResults.Created($"/api/v1/work-logs/{result.WorkLog.Id}", result);
        });
        api.MapPatch("/work-logs/{workLogId:guid}", async (Guid workLogId, UpdateWorkLogRequest body, WorkLogService logs, CancellationToken ct) =>
            TypedResults.Ok(await logs.UpdateAsync(workLogId, body, ct)));
        api.MapDelete("/work-logs/{workLogId:guid}", async (Guid workLogId, WorkLogService logs, CancellationToken ct) =>
        {
            await logs.DeleteAsync(workLogId, ct);
            return TypedResults.NoContent();
        });
    }

    private static void MapGantt(RouteGroupBuilder api)
    {
        // API-19、20
        api.MapGet("/gantt", async (string? teamIds, DateOnly? from, DateOnly? to, GanttQuery gantt, BusinessClock clock, CancellationToken ct) =>
        {
            var today = clock.Today;
            return TypedResults.Ok(await gantt.GetAsync(ParseIds(teamIds), from ?? today.AddDays(-7), to ?? today.AddDays(35), ct));
        });
        api.MapGet("/gantt/search", async (string? teamIds, string? q, GanttQuery gantt, CancellationToken ct) =>
            TypedResults.Ok(await gantt.SearchAsync(ParseIds(teamIds), q, ct)));
    }

    private static void MapViewsAndNotifications(RouteGroupBuilder api)
    {
        // API-45〜48
        api.MapGet("/views", async (ViewService views, CancellationToken ct) => TypedResults.Ok(await views.ListAsync(ct)));
        api.MapPost("/views", async (CreateViewRequest body, ViewService views, CancellationToken ct) =>
            TypedResults.Ok(await views.CreateAsync(body, ct)));
        api.MapPatch("/views/{viewId:guid}", async (Guid viewId, UpdateViewRequest body, ViewService views, CancellationToken ct) =>
            TypedResults.Ok(await views.UpdateAsync(viewId, body, ct)));
        api.MapDelete("/views/{viewId:guid}", async (Guid viewId, ViewService views, CancellationToken ct) =>
        {
            await views.DeleteAsync(viewId, ct);
            return TypedResults.NoContent();
        });

        // API-49〜52（未読の数の確認では延長しない）
        api.MapGet("/notifications", async (string? cursor, int? limit, bool? unreadOnly, NotificationService notifications, CancellationToken ct) =>
            TypedResults.Ok(await notifications.ListAsync(cursor, limit, unreadOnly ?? false, ct)));
        api.MapGet("/notifications/unread-count", async (NotificationService notifications, CancellationToken ct) =>
            TypedResults.Ok(new { count = await notifications.UnreadCountAsync(ct) }));
        api.MapPost("/notifications/{id:guid}/read", async (Guid id, NotificationService notifications, CancellationToken ct) =>
        {
            await notifications.MarkReadAsync(id, ct);
            return TypedResults.NoContent();
        });
        api.MapPost("/notifications/read-all", async (NotificationService notifications, CancellationToken ct) =>
            TypedResults.Ok(new { count = await notifications.MarkAllReadAsync(ct) }));
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        // API-53〜60
        admin.MapGet("/users", async (string? q, string? status, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.ListAsync(q, status, ct)));
        admin.MapPost("/invitations", async (InviteRequest body, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok((await users.InviteAsync(body, ct)).User));
        admin.MapPost("/invitations/{userId:guid}/resend", async (Guid userId, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.ResendInvitationAsync(userId, ct)));
        admin.MapDelete("/invitations/{userId:guid}", async (Guid userId, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.RevokeInvitationAsync(userId, ct)));
        admin.MapPost("/users/{userId:guid}/disable", async (Guid userId, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.DisableAsync(userId, ct)));
        admin.MapPost("/users/{userId:guid}/enable", async (Guid userId, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.EnableAsync(userId, ct)));
        admin.MapPut("/users/{userId:guid}/admin", async (Guid userId, SetAdminRequest body, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.SetAdminAsync(userId, body, ct)));
        admin.MapPost("/users/{userId:guid}/reset-mfa", async (Guid userId, ResetMfaRequest body, AdminUserService users, CancellationToken ct) =>
            TypedResults.Ok(await users.ResetMfaAsync(userId, body, ct)));

        // API-61、62
        admin.MapGet("/teams", async (AdminTeamQuery teams, CancellationToken ct) => TypedResults.Ok(await teams.ListAsync(ct)));
        admin.MapGet("/audit-logs", async ([AsParameters] AuditLogSearch search, AuditLogQuery logs, CancellationToken ct) =>
            TypedResults.Ok(await logs.SearchAsync(search, ct)));

        // API-63〜65
        admin.MapGet("/holidays", async (int? year, HolidayService holidays, CancellationToken ct) =>
            TypedResults.Ok(await holidays.ListAsync(year, ct)));
        admin.MapPost("/holidays", async (HolidayRequest body, HolidayService holidays, CancellationToken ct) =>
            TypedResults.Ok(await holidays.AddAsync(body, ct)));
        admin.MapDelete("/holidays/{date}", async (string date, HolidayService holidays, CancellationToken ct) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                throw new NotFoundException();
            }

            await holidays.DeleteAsync(parsed, ct);
            return TypedResults.NoContent();
        });
    }

    private static List<Guid> ParseIds(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : throw ValidationException.For("teamIds", Msg.CmnChoice))
            .ToList();

    private static object SessionStatus(HttpContext http, SecurityOptions options, TimeProvider time)
    {
        var session = SessionInfo.From(http) ?? throw new UnauthenticatedException();
        var now = time.GetUtcNow().UtcDateTime;
        var idleEnds = session.LastSeenAt.AddMinutes(options.Session.IdleMinutes);
        var ends = idleEnds < session.ExpiresAt ? idleEnds : session.ExpiresAt;
        return new
        {
            remainingSeconds = Math.Max(0, (int)(ends - now).TotalSeconds),
            absoluteRemainingSeconds = Math.Max(0, (int)(session.ExpiresAt - now).TotalSeconds),
            warningSeconds = options.Session.WarningMinutes * 60,
            authMethod = session.AuthMethod,
        };
    }
}
