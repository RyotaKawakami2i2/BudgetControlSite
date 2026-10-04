using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Application.Teams;

/// <summary>チームのメンバーと役割（FR-TEM-01、02、07、FR-ADM-05）。</summary>
public sealed class MembershipService(
    IAppDbContext db,
    AccessPolicy access,
    TeamService teams,
    TaskHistoryWriter history,
    Notifier notifier,
    IAuditWriter audit,
    IDbLocks locks,
    BusinessClock clock)
{
    public async Task<TeamDetailDto> AddAsync(Guid teamId, AddMemberRequest request, CancellationToken ct)
    {
        var v = new Validation();
        var role = TeamRole.Member;
        if (request.Role is not null && v.TryCode<TeamRole>("role", request.Role, out var r))
        {
            role = r.Value;
        }

        v.ThrowIfAny();
        var acc = await access.RequireTeamAsync(teamId, Operation.TeamMemberAdd, ct);
        if (role == TeamRole.Leader)
        {
            await access.EnsureAsync(Operation.TeamLeaderAssign, acc.Facts, "team", teamId.ToString(), teamId, ct);
        }

        var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (target is null || target.Status != UserStatus.Active)
        {
            // 追加できるのは有効な利用者だけ
            throw ValidationException.For("userId", Msg.CmnChoice);
        }

        var actorId = access.UserId;
        var now = clock.UtcNow;
        var row = await db.TeamMembers.SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == request.UserId, ct);
        if (row is { RemovedAt: null })
        {
            return await teams.GetAsync(teamId, ct);
        }

        if (row is null)
        {
            db.TeamMembers.Add(new TeamMember { TeamId = teamId, UserId = request.UserId, Role = role, JoinedAt = now });
        }
        else
        {
            // 再び追加するときは、同じ行の removed_at を空に戻す
            row.RemovedAt = null;
            row.RemovedBy = null;
            row.Role = role;
            row.JoinedAt = now;
        }

        notifier.Notify(request.UserId, NotificationKind.TeamAdded, actorId, teamId, null);
        audit.Add(new AuditEntry("team.member.added", TargetType: "team", TargetId: teamId.ToString(), TeamId: teamId,
            Detail: new { userId = request.UserId, role = role.ToCode() }));
        await db.SaveChangesAsync(ct);
        return await teams.GetAsync(teamId, ct);
    }

    public async Task<TeamDetailDto> ChangeRoleAsync(Guid teamId, Guid userId, ChangeRoleRequest request, CancellationToken ct)
    {
        var v = new Validation();
        v.TryCode<TeamRole>("role", request.Role, out var newRole);
        v.ThrowIfAny();

        var operation = newRole == TeamRole.Leader ? Operation.TeamLeaderAssign : Operation.TeamLeaderRevoke;
        await access.RequireTeamAsync(teamId, operation, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockAsync($"team-leaders:{teamId}", ct);
        var row = await db.TeamMembers.SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId && m.RemovedAt == null, ct)
            ?? throw new NotFoundException();
        if (row.Role == newRole)
        {
            return await teams.GetAsync(teamId, ct);
        }

        if (row.Role == TeamRole.Leader)
        {
            await EnsureNotLastLeaderAsync(teamId, userId, ct);
        }

        var before = row.Role;
        row.Role = newRole!.Value;
        audit.Add(new AuditEntry("team.member.role_changed", TargetType: "team", TargetId: teamId.ToString(), TeamId: teamId,
            Detail: new { userId, from = before.ToCode(), to = row.Role.ToCode() }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await teams.GetAsync(teamId, ct);
    }

    public async Task<TeamDetailDto> RemoveAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        await access.RequireTeamAsync(teamId, Operation.TeamMemberRemove, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockAsync($"team-leaders:{teamId}", ct);
        var row = await db.TeamMembers.SingleOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId && m.RemovedAt == null, ct)
            ?? throw new NotFoundException();
        if (row.Role == TeamRole.Leader)
        {
            await EnsureNotLastLeaderAsync(teamId, userId, ct);
        }

        await RemoveMembershipAsync(row, access.UserId, "removed", ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await teams.GetAsync(teamId, ct);
    }

    /// <summary>
    /// チームから外す（詳細設計書 4.9）。担当していた未完了のタスクは未割り当てに戻し、リーダーに通知する。
    /// 完了したタスクの担当者と作業実績はそのまま残す。アカウントの無効化でも使う。
    /// </summary>
    internal async Task RemoveMembershipAsync(TeamMember row, Guid actorId, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        row.RemovedAt = now;
        row.RemovedBy = actorId;

        var tasks = await db.Tasks
            .Where(t => t.TeamId == row.TeamId && t.AssigneeId == row.UserId && t.DeletedAt == null
                && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled)
            .ToListAsync(ct);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == row.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct) ?? "";
        var leaders = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == row.TeamId && m.RemovedAt == null && m.Role == TeamRole.Leader && m.UserId != row.UserId)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        foreach (var task in tasks)
        {
            task.AssigneeId = null;
            task.Version++;
            task.UpdatedAt = now;
            task.UpdatedBy = actorId;
            history.Add(task, actorId, TaskHistoryKind.Updated, "assigneeId", name, "未割り当て");
            foreach (var leader in leaders)
            {
                notifier.Notify(leader, NotificationKind.TaskUnassigned, actorId, task.TeamId, task.Id);
            }
        }

        audit.Add(new AuditEntry("team.member.removed", TargetType: "team", TargetId: row.TeamId.ToString(), TeamId: row.TeamId,
            Detail: new { userId = row.UserId, reason, unassignedTasks = tasks.Count }));
    }

    private async Task EnsureNotLastLeaderAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        var others = await db.TeamMembers.CountAsync(
            m => m.TeamId == teamId && m.RemovedAt == null && m.Role == TeamRole.Leader && m.UserId != userId, ct);
        if (others == 0)
        {
            throw new RuleViolationException(Msg.TemLastLeader);
        }
    }
}
