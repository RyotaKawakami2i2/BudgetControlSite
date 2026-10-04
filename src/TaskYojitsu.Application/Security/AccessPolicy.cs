using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Application.Security;

/// <summary>操作者の情報（要求ごとに 1 回だけ DB から読む）。</summary>
public sealed record UserFacts(Guid UserId, bool IsActive, bool IsAdmin, bool IsLeaderAnywhere, string DisplayName);

/// <summary>判定を通ったチームの情報。</summary>
public sealed record TeamAccess(Team Team, TeamRole? Role, AccessFacts Facts)
{
    /// <summary>管理者として、所属していないチームを閲覧している。</summary>
    public bool IsAdminView => Role is null;
}

/// <summary>判定を通ったタスクの情報。</summary>
public sealed record TaskAccess(TaskItem Task, TeamAccess Team, AccessFacts Facts);

/// <summary>
/// 権限の判定（NF-ACC-02、NF-ACC-05）。判定の材料は毎回 DB から読む（キャッシュしない。NF-SES-05）。
/// 所属していないチームのデータは 404、所属しているが権限がない場合は 403 を返す。拒否したことは監査ログに残す。
/// </summary>
public sealed class AccessPolicy(
    IAppDbContext db,
    IRequestContext request,
    IAuditWriter audit,
    IMemoryCache cache,
    BusinessClock clock,
    IOptions<SecurityOptions> securityOptions)
{
    private UserFacts? _user;

    /// <summary>操作者の ID。未ログインなら 401。</summary>
    public Guid UserId => request.UserId ?? throw new UnauthenticatedException();

    /// <summary>操作者の ID（運用コマンドなど、ログインしていない場合は null）。</summary>
    public Guid? UserIdOrNull => request.UserId;

    /// <summary>管理者の操作の条件（直近の再認証、許可したネットワーク、必要なら多要素認証）を満たすか。</summary>
    public bool AdminStepUp
    {
        get
        {
            var options = securityOptions.Value;
            var recent = request.AuthTime is { } authTime
                && clock.UtcNow - authTime <= TimeSpan.FromMinutes(options.Reauth.Minutes);
            return recent && request.IsFromAdminNetwork;
        }
    }

    public async Task<UserFacts> GetUserAsync(CancellationToken ct)
    {
        var userId = UserId;
        if (_user is not null && _user.UserId == userId)
        {
            return _user;
        }

        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Status, u.IsAdmin, u.DisplayName })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            throw new UnauthenticatedException();
        }

        var leaderAnywhere = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.RemovedAt == null && m.Role == TeamRole.Leader, ct);

        _user = new UserFacts(userId, row.Status == UserStatus.Active, row.IsAdmin, leaderAnywhere, row.DisplayName);
        return _user;
    }

    /// <summary>チームの基本の材料（A、R、X、L）を読む。チームがなければ null。</summary>
    public async Task<(Team Team, AccessFacts Facts)?> LoadTeamFactsAsync(Guid teamId, bool track, CancellationToken ct)
    {
        var user = await GetUserAsync(ct);
        var query = track ? db.Teams : db.Teams.AsNoTracking();
        var team = await query.SingleOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null)
        {
            return null;
        }

        var role = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.UserId == user.UserId && m.RemovedAt == null)
            .Select(m => (TeamRole?)m.Role)
            .SingleOrDefaultAsync(ct);

        var facts = new AccessFacts
        {
            IsActiveUser = user.IsActive,
            IsAdmin = user.IsAdmin,
            Role = role,
            IsArchived = team.IsArchived,
            IsLeaderAnywhere = user.IsLeaderAnywhere,
            AdminStepUp = AdminStepUp,
        };
        return (team, facts);
    }

    /// <summary>チームに対する操作を判定する。許可しなければ例外を投げる。</summary>
    public async Task<TeamAccess> RequireTeamAsync(Guid teamId, Operation operation, CancellationToken ct, bool track = false)
    {
        var loaded = await LoadTeamFactsAsync(teamId, track, ct);
        if (loaded is null)
        {
            await DenyAsync(AccessOutcome.NotFound, operation, "team", teamId.ToString(), null, ct);
            throw new NotFoundException();
        }

        var (team, facts) = loaded.Value;
        await EnsureAsync(operation, facts, "team", teamId.ToString(), teamId, ct);
        return new TeamAccess(team, facts.Role, facts);
    }

    /// <summary>
    /// タスクに対する操作を判定する。論理削除したタスクは見つからない扱い（includeDeleted で含められる）。
    /// adjust で、操作に固有の材料（W、もう一方のタスクなど）を加える。
    /// </summary>
    public async Task<TaskAccess> RequireTaskAsync(
        Guid taskId,
        Operation operation,
        CancellationToken ct,
        Func<TaskItem, AccessFacts, Task<AccessFacts>>? adjust = null,
        bool track = true,
        bool includeDeleted = false)
    {
        var query = track ? db.Tasks : db.Tasks.AsNoTracking();
        var task = await query.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null || (task.DeletedAt is not null && !includeDeleted))
        {
            await DenyAsync(AccessOutcome.NotFound, operation, "task", taskId.ToString(), null, ct);
            throw new NotFoundException();
        }

        var loaded = await LoadTeamFactsAsync(task.TeamId, track: false, ct)
            ?? throw new NotFoundException();
        var (team, teamFacts) = loaded;
        var facts = teamFacts with
        {
            IsCreator = task.CreatedBy == UserId,
            IsAssignee = task.AssigneeId == UserId,
        };
        if (adjust is not null)
        {
            facts = await adjust(task, facts);
        }

        await EnsureAsync(operation, facts, "task", taskId.ToString(), task.TeamId, ct);
        return new TaskAccess(task, new TeamAccess(team, teamFacts.Role, teamFacts), facts);
    }

    /// <summary>判定して、許可しなければ監査ログに残して例外を投げる。管理者の閲覧も記録する。</summary>
    public async Task EnsureAsync(Operation operation, AccessFacts facts, string targetType, string targetId, Guid? teamId, CancellationToken ct)
    {
        AccessOutcome outcome;
        try
        {
            outcome = AccessRules.Evaluate(operation, facts);
        }
        catch (Exception)
        {
            // 判定の途中でエラーが起きたら拒否する（NF-ACC-02）
            outcome = AccessOutcome.Forbidden;
        }

        if (outcome != AccessOutcome.Allow)
        {
            await DenyAsync(outcome, operation, targetType, targetId, teamId, ct);
            throw outcome switch
            {
                AccessOutcome.NotFound => new NotFoundException(),
                AccessOutcome.Archived => ForbiddenException.Archived(),
                AccessOutcome.ReauthRequired => ForbiddenException.ReauthRequired(),
                _ => new ForbiddenException(),
            };
        }

        if (facts.Role is null && facts.IsAdmin && teamId is { } viewedTeam && AccessRules.IsViewOperation(operation))
        {
            await RecordAdminViewAsync(viewedTeam, ct);
        }
    }

    /// <summary>判定だけを行う（画面に出す操作の可否の計算に使う）。</summary>
    public static bool Can(Operation operation, AccessFacts facts) =>
        AccessRules.Evaluate(operation, facts) == AccessOutcome.Allow;

    /// <summary>管理者が、所属していないチームを閲覧したことを記録する（同じセッション・同じチームでは 1 回だけ）。</summary>
    private async Task RecordAdminViewAsync(Guid teamId, CancellationToken ct)
    {
        var key = $"admin-view:{request.SessionId}:{teamId}";
        if (cache.TryGetValue(key, out _))
        {
            return;
        }

        cache.Set(key, true, TimeSpan.FromHours(securityOptions.Value.Session.AbsoluteHours));
        await audit.WriteNowAsync(new AuditEntry("admin.team_viewed", TargetType: "team", TargetId: teamId.ToString(), TeamId: teamId), ct);
    }

    private async Task DenyAsync(AccessOutcome outcome, Operation operation, string targetType, string targetId, Guid? teamId, CancellationToken ct)
    {
        await audit.WriteNowAsync(new AuditEntry(
            "access.denied",
            AuditResult.Denied,
            targetType,
            targetId,
            teamId,
            new { operation = operation.ToString(), outcome = outcome.ToString() }), ct);
    }
}
