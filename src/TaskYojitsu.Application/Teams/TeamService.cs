using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Teams;

/// <summary>チーム（プロジェクト）の作成・変更・アーカイブ（FR-TEM-04〜06、FR-ADM-04）。</summary>
public sealed class TeamService(
    IAppDbContext db,
    AccessPolicy access,
    TaskDataLoader loader,
    Notifier notifier,
    IAuditWriter audit,
    BusinessClock clock)
{
    public async Task<IReadOnlyList<TeamSummaryDto>> ListAsync(bool includeArchived, bool all, CancellationToken ct)
    {
        var user = await access.GetUserAsync(ct);
        var mine = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == user.UserId && m.RemovedAt == null)
            .ToDictionaryAsync(m => m.TeamId, m => m.Role, ct);

        // 管理者は scope=all で全チームを取得できる（所属していないチームの役割は admin_view）
        var query = db.Teams.AsNoTracking();
        if (!(all && user.IsAdmin))
        {
            var ids = mine.Keys.ToList();
            query = query.Where(t => ids.Contains(t.Id));
        }

        if (!includeArchived)
        {
            query = query.Where(t => t.ArchivedAt == null);
        }

        var teams = await query
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.ArchivedAt,
                t.Version,
                MemberCount = db.TeamMembers.Count(m => m.TeamId == t.Id && m.RemovedAt == null),
            })
            .ToListAsync(ct);
        return [.. teams
            .OrderBy(t => t.ArchivedAt is not null)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new TeamSummaryDto(
                t.Id, t.Name, t.Description, t.ArchivedAt is not null,
                mine.TryGetValue(t.Id, out var role) ? role.ToCode() : "admin_view",
                t.MemberCount, t.Version))];
    }

    public async Task<TeamDetailDto> GetAsync(Guid teamId, CancellationToken ct)
    {
        var acc = await access.RequireTeamAsync(teamId, Operation.TeamView, ct);
        var showEmail = acc.Role == TeamRole.Leader || acc.Facts.IsAdmin;
        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.RemovedAt == null)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u.DisplayName, u.Email, u.Status })
            .ToListAsync(ct);
        var tags = await loader.LoadTagsAsync([teamId], ct);
        var f = acc.Facts;
        var can = new TeamCan(
            Update: AccessPolicy.Can(Operation.TeamUpdate, f with { AdminStepUp = true }),
            Archive: !acc.Team.IsArchived && AccessPolicy.Can(Operation.TeamArchive, f with { AdminStepUp = true }),
            Unarchive: acc.Team.IsArchived && AccessPolicy.Can(Operation.TeamUnarchive, f with { AdminStepUp = true }),
            ManageMembers: AccessPolicy.Can(Operation.TeamMemberAdd, f with { AdminStepUp = true }),
            ManageTags: AccessPolicy.Can(Operation.TeamTagManage, f),
            ManageSharedViews: AccessPolicy.Can(Operation.TeamSharedViewManage, f),
            ViewReport: AccessPolicy.Can(Operation.ReportTeam, f),
            CreateTask: AccessPolicy.Can(Operation.TaskCreate, f),
            Restore: AccessPolicy.Can(Operation.TaskRestore, f));
        return new TeamDetailDto(
            acc.Team.Id,
            acc.Team.Name,
            acc.Team.Description,
            acc.Team.IsArchived,
            acc.Team.ArchivedAt,
            acc.Team.Version,
            acc.Role?.ToCode() ?? "admin_view",
            [.. members
                .OrderBy(m => m.m.Role)
                .ThenBy(m => m.DisplayName, StringComparer.Ordinal)
                .Select(m => new TeamMemberDto(m.m.UserId, m.DisplayName, showEmail ? m.Email : null, m.m.Role, m.m.JoinedAt,
                    m.Status == UserStatus.Disabled))],
            tags,
            can);
    }

    public async Task<TeamDetailDto> CreateAsync(CreateTeamRequest request, CancellationToken ct)
    {
        var user = await access.GetUserAsync(ct);
        var v = new Validation();
        var name = v.SingleLine("name", request.Name, Limits.TeamNameMax, required: true, Msg.TemName, Msg.TemName);
        var description = v.Multiline("description", request.Description, Limits.TeamDescriptionMax);

        await access.EnsureAsync(Operation.TeamCreate, new AccessFacts
        {
            IsActiveUser = user.IsActive,
            IsAdmin = user.IsAdmin,
            IsLeaderAnywhere = user.IsLeaderAnywhere,
        }, "team", "new", null, ct);

        Guid leaderId;
        if (request.LeaderUserId is { } specified)
        {
            // 管理者が作る場合は、作成と同時にリーダーを指定する（再認証が必要。FR-ADM-08）
            if (!user.IsAdmin)
            {
                throw new ForbiddenException();
            }

            if (!access.AdminStepUp)
            {
                throw ForbiddenException.ReauthRequired();
            }

            var leader = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == specified && u.Status == UserStatus.Active, ct);
            if (leader is null)
            {
                v.Add("leaderUserId", Msg.CmnChoice);
            }

            leaderId = specified;
        }
        else if (user.IsLeaderAnywhere)
        {
            // リーダーが作る場合は、作った人がリーダーになる
            leaderId = user.UserId;
        }
        else
        {
            v.Add("leaderUserId", Msg.CmnRequired);
            leaderId = Guid.Empty;
        }

        v.ThrowIfAny();
        await EnsureUniqueNameAsync(name!, null, ct);

        var now = clock.UtcNow;
        var team = new Team
        {
            Id = Guid.CreateVersion7(),
            Name = name!,
            Description = description,
            Version = 1,
            CreatedAt = now,
            CreatedBy = user.UserId,
            UpdatedAt = now,
            UpdatedBy = user.UserId,
        };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember { TeamId = team.Id, UserId = leaderId, Role = TeamRole.Leader, JoinedAt = now });
        notifier.Notify(leaderId, NotificationKind.TeamAdded, user.UserId, team.Id, null);
        audit.Add(new AuditEntry("team.created", TargetType: "team", TargetId: team.Id.ToString(), TeamId: team.Id,
            Detail: new { name, leaderUserId = leaderId }));
        audit.Add(new AuditEntry("team.member.added", TargetType: "team", TargetId: team.Id.ToString(), TeamId: team.Id,
            Detail: new { userId = leaderId, role = "leader" }));
        await SaveUniqueAsync(ct);
        return await GetAsync(team.Id, ct);
    }

    public async Task<TeamDetailDto> UpdateAsync(Guid teamId, UpdateTeamRequest request, CancellationToken ct)
    {
        var acc = await access.RequireTeamAsync(teamId, Operation.TeamUpdate, ct, track: true);
        var team = acc.Team;
        if (request.Version != team.Version)
        {
            throw new ConflictException(await GetAsync(teamId, ct));
        }

        var v = new Validation();
        var name = request.Name.HasValue
            ? v.SingleLine("name", request.Name.Value, Limits.TeamNameMax, required: true, Msg.TemName, Msg.TemName)
            : team.Name;
        var description = request.Description.HasValue
            ? v.Multiline("description", request.Description.Value, Limits.TeamDescriptionMax)
            : team.Description;
        v.ThrowIfAny();
        if (!string.Equals(name, team.Name, StringComparison.Ordinal))
        {
            await EnsureUniqueNameAsync(name!, teamId, ct);
        }

        var before = new { team.Name, team.Description };
        team.Name = name!;
        team.Description = description;
        team.Version++;
        team.UpdatedAt = clock.UtcNow;
        team.UpdatedBy = access.UserId;
        audit.Add(new AuditEntry("team.updated", TargetType: "team", TargetId: teamId.ToString(), TeamId: teamId,
            Detail: new { from = before, to = new { team.Name, team.Description } }));
        await SaveUniqueAsync(ct);
        return await GetAsync(teamId, ct);
    }

    public async Task<TeamDetailDto> SetArchivedAsync(Guid teamId, bool archived, int? version, CancellationToken ct)
    {
        var acc = await access.RequireTeamAsync(teamId, archived ? Operation.TeamArchive : Operation.TeamUnarchive, ct, track: true);
        var team = acc.Team;
        if (version is { } expected && expected != team.Version)
        {
            throw new ConflictException(await GetAsync(teamId, ct));
        }

        if (team.IsArchived != archived)
        {
            var now = clock.UtcNow;
            team.ArchivedAt = archived ? now : null;
            team.ArchivedBy = archived ? access.UserId : null;
            team.Version++;
            team.UpdatedAt = now;
            team.UpdatedBy = access.UserId;
            audit.Add(new AuditEntry(archived ? "team.archived" : "team.unarchived", TargetType: "team", TargetId: teamId.ToString(), TeamId: teamId));
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(teamId, ct);
    }

    /// <summary>有効な利用者の検索（API-15）。管理者か、いずれかのチームのリーダーだけが使える。</summary>
    public async Task<IReadOnlyList<UserSearchDto>> SearchUsersAsync(string? q, CancellationToken ct)
    {
        var user = await access.GetUserAsync(ct);
        if (!user.IsActive || !(user.IsAdmin || user.IsLeaderAnywhere))
        {
            throw new ForbiddenException();
        }

        var keyword = TextRules.NormalizeSingleLine(q);
        if (keyword is null || TextRules.Length(keyword) < 2)
        {
            return [];
        }

        if (TextRules.Length(keyword) > 100)
        {
            throw ValidationException.For("q", Msg.CmnTooLong);
        }

        var lower = keyword.ToLowerInvariant();
        return await db.Users.AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                && (u.DisplayName.ToLower().Contains(lower) || (u.Email != null && u.Email.ToLower().Contains(lower))))
            .OrderBy(u => u.DisplayName)
            .Take(20)
            .Select(u => new UserSearchDto(u.Id, u.DisplayName, u.Email ?? ""))
            .ToListAsync(ct);
    }

    private async Task EnsureUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        if (await db.Teams.AnyAsync(t => t.Name.ToLower() == lower && t.Id != exceptId, ct))
        {
            throw new RuleViolationException(Msg.TemNameDuplicate);
        }
    }

    private async Task SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_teams_lower_name", StringComparison.Ordinal) == true)
        {
            // 同時に同じ名前で作った場合（DB の一意の索引）
            throw new RuleViolationException(Msg.TemNameDuplicate);
        }
    }
}
