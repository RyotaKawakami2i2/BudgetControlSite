using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Audit;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Web.Jobs;

/// <summary>JOB-01 期限の通知（毎日 7:00）。予定終了日が明日のタスクと、期限を超えたタスクの担当者に通知する（同じタスクは 1 日 1 回）。</summary>
public sealed class DueNotificationJob : IScheduledJob
{
    public string Id => "JOB-01";

    public JobSchedule Schedule { get; } = new JobSchedule.Daily(7, 0);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var today = clock.Today;
        var tomorrow = today.AddDays(1);
        var startOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(TimeOnly.MinValue), clock.TimeZone);

        var targets = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.AssigneeId != null
                && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                && t.PlannedEnd != null && (t.PlannedEnd == tomorrow || t.PlannedEnd < today)
                && !db.Tasks.Any(c => c.ParentId == t.Id && c.DeletedAt == null)
                && db.Teams.Any(team => team.Id == t.TeamId && team.ArchivedAt == null))
            .Select(t => new { t.Id, t.TeamId, AssigneeId = t.AssigneeId!.Value, t.PlannedEnd })
            .ToListAsync(ct);
        var already = await db.Notifications.AsNoTracking()
            .Where(n => n.CreatedAt >= startOfDayUtc && (n.Kind == NotificationKind.DueTomorrow || n.Kind == NotificationKind.Overdue))
            .Select(n => new { n.TaskId, n.UserId, n.Kind })
            .ToListAsync(ct);
        var sent = already.Select(a => (a.TaskId, a.UserId, a.Kind)).ToHashSet();

        var now = clock.UtcNow;
        foreach (var t in targets)
        {
            var kind = t.PlannedEnd == tomorrow ? NotificationKind.DueTomorrow : NotificationKind.Overdue;
            if (sent.Contains((t.Id, t.AssigneeId, kind)))
            {
                continue;
            }

            db.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                UserId = t.AssigneeId,
                Kind = kind,
                TeamId = t.TeamId,
                TaskId = t.Id,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>JOB-02 削除したタスクの整理（毎日 2:00）。論理削除から 365 日を過ぎたタスクを完全に削除する（部分木ごと）。</summary>
public sealed class PurgeDeletedTasksJob : IScheduledJob
{
    public string Id => "JOB-02";

    public JobSchedule Schedule { get; } = new JobSchedule.Daily(2, 0);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var audit = services.GetRequiredService<IAuditWriter>();
        var threshold = clock.UtcNow.AddDays(-JobOptions.Business(services).SoftDeleteRetentionDays);
        var batches = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt != null && t.DeletedAt < threshold)
            .Select(t => new { t.Id, t.TeamId, t.DeleteBatchId, t.ParentId })
            .ToListAsync(ct);

        foreach (var batch in batches.GroupBy(b => b.DeleteBatchId))
        {
            var ids = batch.Select(b => b.Id).ToList();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // 子から先に消す（親の外部キーは ON DELETE CASCADE だが、念のため同じまとまりを一度に消す）
            var deleted = await db.Tasks.Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(ct);
            audit.Add(new AuditEntry("task.purged", TargetType: "task", TargetId: batch.First().Id.ToString(),
                TeamId: batch.First().TeamId, Detail: new { count = deleted, batchId = batch.Key }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}

/// <summary>JOB-03 期限切れのデータの削除（1 時間ごと）。期限切れの招待と再設定のトークン、終了してから 7 日たったセッション。</summary>
public sealed class CleanupExpiredJob : IScheduledJob
{
    public string Id => "JOB-03";

    public JobSchedule Schedule { get; } = new JobSchedule.Every(TimeSpan.FromHours(1));

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        await db.Invitations.Where(i => i.ExpiresAt < now || i.UsedAt != null || i.RevokedAt != null)
            .Where(i => i.CreatedAt < weekAgo || i.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
        await db.PasswordResetTokens.Where(t => t.ExpiresAt < now).ExecuteDeleteAsync(ct);
        await db.UserSessions
            .Where(s => (s.RevokedAt != null && s.RevokedAt < weekAgo) || s.ExpiresAt < weekAgo)
            .ExecuteDeleteAsync(ct);
    }
}

/// <summary>JOB-04 監査ログの検証（毎日 3:00）。直近 1 日分の連鎖を検証する。日曜日は全体を検証する。異常があれば管理者に知らせる。</summary>
public sealed class AuditVerifyJob : IScheduledJob
{
    public string Id => "JOB-04";

    public JobSchedule Schedule { get; } = new JobSchedule.Daily(3, 0);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var verifier = services.GetRequiredService<AuditChainVerifier>();
        var full = clock.Today.DayOfWeek == DayOfWeek.Sunday;
        long? from = null;
        if (!full)
        {
            var since = clock.UtcNow.AddDays(-1);
            from = await db.AuditLogs.AsNoTracking().Where(a => a.OccurredAt < since).MaxAsync(a => (long?)a.Id, ct);
        }

        var result = await verifier.VerifyAsync(from, ct);
        var audit = services.GetRequiredService<IAuditWriter>();
        await audit.WriteNowAsync(new AuditEntry("audit.verified", result.Ok ? AuditResult.Success : AuditResult.Failure,
            Detail: new { full, result.Checked, result.FirstBrokenId, result.Reason }), ct);
        if (!result.Ok)
        {
            await AdminAlert.SendAsync(services, "監査ログの改ざんの疑い",
                $"監査ログのハッシュ連鎖が途切れています。最初に食い違った記録の ID: {result.FirstBrokenId}（{result.Reason}）", ct);
        }
    }
}

/// <summary>JOB-05 セキュリティの検知（5 分ごと）。ログイン失敗の急増、ロックの多発、管理者権限の変更を管理者に知らせる（NF-LOG-06）。</summary>
public sealed class SecurityDetectionJob : IScheduledJob
{
    public string Id => "JOB-05";

    public JobSchedule Schedule { get; } = new JobSchedule.Every(TimeSpan.FromMinutes(5));

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var now = clock.UtcNow;
        var fiveMinutes = now.AddMinutes(-5);
        var fifteenMinutes = now.AddMinutes(-15);

        var failures = await db.AuditLogs.CountAsync(a => a.Action == "auth.login" && a.Result == AuditResult.Failure && a.OccurredAt >= fiveMinutes, ct);
        var locks = await db.AuditLogs.CountAsync(a => a.Action == "auth.login.locked" && a.OccurredAt >= fifteenMinutes, ct);
        var adminChanges = await db.AuditLogs.CountAsync(
            a => (a.Action == "user.admin.granted" || a.Action == "user.admin.revoked") && a.OccurredAt >= fiveMinutes, ct);

        var alerts = new List<string>();
        if (failures >= 30)
        {
            alerts.Add($"直近 5 分のログイン失敗が {failures} 回あります");
        }

        if (locks >= 3)
        {
            alerts.Add($"直近 15 分にアカウントのロックが {locks} 件あります");
        }

        if (adminChanges > 0)
        {
            alerts.Add($"直近 5 分に管理者権限の変更が {adminChanges} 件あります");
        }

        if (alerts.Count == 0)
        {
            return;
        }

        var audit = services.GetRequiredService<IAuditWriter>();
        await audit.WriteNowAsync(new AuditEntry("security.alert", Detail: new { alerts }), ct);
        await AdminAlert.SendAsync(services, alerts[0], string.Join("\n", alerts), ct);
    }
}

/// <summary>JOB-06 古い通知の削除（毎日 4:00）。作成から 180 日を過ぎた通知。</summary>
public sealed class NotificationCleanupJob : IScheduledJob
{
    public string Id => "JOB-06";

    public JobSchedule Schedule { get; } = new JobSchedule.Daily(4, 0);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var threshold = clock.UtcNow.AddDays(-JobOptions.Business(services).NotificationRetentionDays);
        await db.Notifications.Where(n => n.CreatedAt < threshold).ExecuteDeleteAsync(ct);
    }
}

internal static class AdminAlert
{
    /// <summary>有効な管理者全員にメールで知らせる。</summary>
    public static async Task SendAsync(IServiceProvider services, string summary, string detail, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var mailer = services.GetRequiredService<Mailer>();
        var admins = await db.Users.AsNoTracking()
            .Where(u => u.IsAdmin && u.Status == UserStatus.Active)
            .Select(u => u.Email)
            .ToListAsync(ct);
        foreach (var email in admins)
        {
            await mailer.SendAsync(email, EmailTemplates.SecurityAlert(summary, detail), ct);
        }
    }
}
