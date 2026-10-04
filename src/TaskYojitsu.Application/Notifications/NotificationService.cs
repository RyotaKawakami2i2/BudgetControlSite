using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Application.Notifications;

/// <summary>
/// 通知の 1 件。タスクの名前などは保存せず、表示するたびに権限を確かめて読み出す。
/// 見られない場合は taskVisible が false になり、画面は「（閲覧できません）」と出す。
/// </summary>
public sealed record NotificationDto(
    Guid Id,
    NotificationKind Kind,
    Guid? TeamId,
    string? TeamName,
    Guid? TaskId,
    string? TaskTitle,
    bool TaskVisible,
    string? ActorName,
    DateTime CreatedAt,
    DateTime? ReadAt);

/// <summary>画面内の通知（FR-NTF-01、02。API-49〜52）。</summary>
public sealed class NotificationService(IAppDbContext db, AccessPolicy access, BusinessClock clock)
{
    public async Task<CursorPage<NotificationDto>> ListAsync(string? cursor, int? limit, bool unreadOnly, CancellationToken ct)
    {
        var user = await access.GetUserAsync(ct);
        var take = Math.Clamp(limit ?? 30, 1, 100);
        var query = db.Notifications.AsNoTracking().Where(n => n.UserId == user.UserId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        // 続きは件数で指定する（通知は 180 日で消えるため、件数は多くならない）
        var skip = int.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset)
            ? Math.Max(offset, 0)
            : 0;
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(skip).Take(take + 1).ToListAsync(ct);
        var page = rows.Take(take).ToList();

        var memberTeams = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == user.UserId && m.RemovedAt == null)
            .Select(m => m.TeamId)
            .ToListAsync(ct);
        var teamIds = page.Where(n => n.TeamId is not null).Select(n => n.TeamId!.Value).Distinct().ToList();
        var taskIds = page.Where(n => n.TaskId is not null).Select(n => n.TaskId!.Value).Distinct().ToList();
        var actorIds = page.Where(n => n.ActorId is not null).Select(n => n.ActorId!.Value).Distinct().ToList();
        var teams = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id) && t.DeletedAt == null)
            .Select(t => new { t.Id, t.Title, t.TeamId })
            .ToDictionaryAsync(t => t.Id, ct);
        var actors = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = page.Select(n =>
        {
            var visibleTeam = n.TeamId is { } teamId && (memberTeams.Contains(teamId) || user.IsAdmin);
            var task = n.TaskId is { } taskId ? tasks.GetValueOrDefault(taskId) : null;
            var taskVisible = task is not null && (memberTeams.Contains(task.TeamId) || user.IsAdmin);
            return new NotificationDto(
                n.Id,
                n.Kind,
                n.TeamId,
                visibleTeam ? teams.GetValueOrDefault(n.TeamId!.Value) : null,
                n.TaskId,
                taskVisible ? task!.Title : null,
                taskVisible,
                n.ActorId is { } actor ? actors.GetValueOrDefault(actor) : null,
                n.CreatedAt,
                n.ReadAt);
        }).ToList();
        return new CursorPage<NotificationDto>(
            items,
            rows.Count > take ? (skip + take).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
    }

    public Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var userId = access.UserId;
        return db.Notifications.AsNoTracking().CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var userId = access.UserId;
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new NotFoundException();
        notification.ReadAt ??= clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var userId = access.UserId;
        var now = clock.UtcNow;
        return await db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }
}
