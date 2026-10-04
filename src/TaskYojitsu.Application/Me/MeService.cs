using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Me;

public sealed record MyTeamDto(Guid Id, string Name, string Role, bool Archived);

/// <summary>
/// 自分の情報（API-01）。表示名、管理者か、チームを作れるか、多要素認証を設定しているか、所属チームと役割、最初に開くビュー。
/// </summary>
public sealed record MeDto(
    Guid Id,
    string DisplayName,
    string Email,
    bool IsAdmin,
    bool CanCreateTeam,
    bool MfaEnabled,
    bool HasAuthenticator,
    int PasskeyCount,
    IReadOnlyList<MyTeamDto> Teams,
    Guid? DefaultViewId);

public sealed record UpdateMeRequest(string? DisplayName);

/// <summary>自分の情報と表示名の変更（FR-AUT-09。API-01、02）。</summary>
public sealed class MeService(IAppDbContext db, AccessPolicy access, IAuditWriter audit, BusinessClock clock)
{
    public async Task<MeDto> GetAsync(CancellationToken ct)
    {
        var facts = await access.GetUserAsync(ct);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == facts.UserId, ct);
        var teams = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == facts.UserId && m.RemovedAt == null)
            .Join(db.Teams, m => m.TeamId, t => t.Id, (m, t) => new { t.Id, t.Name, m.Role, t.ArchivedAt })
            .ToListAsync(ct);
        var passkeys = await db.UserPasskeys.AsNoTracking().CountAsync(p => p.UserId == facts.UserId, ct);
        return new MeDto(
            user.Id,
            user.DisplayName,
            user.Email ?? "",
            user.IsAdmin,
            facts.IsActive && (user.IsAdmin || facts.IsLeaderAnywhere),
            user.TwoFactorEnabled || passkeys > 0,
            user.TwoFactorEnabled,
            passkeys,
            [.. teams
                .OrderBy(t => t.ArchivedAt is not null)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => new MyTeamDto(t.Id, t.Name, t.Role.ToCode(), t.ArchivedAt is not null))],
            user.DefaultViewId);
    }

    public async Task<MeDto> UpdateAsync(UpdateMeRequest request, CancellationToken ct)
    {
        var v = new Validation();
        var name = v.SingleLine("displayName", request.DisplayName, Limits.DisplayNameMax, required: true);
        v.ThrowIfAny();
        var user = await db.Users.SingleAsync(u => u.Id == access.UserId, ct);
        if (!string.Equals(user.DisplayName, name, StringComparison.Ordinal))
        {
            var before = user.DisplayName;
            user.DisplayName = name!;
            user.UpdatedAt = clock.UtcNow;
            audit.Add(new AuditEntry("user.display_name.changed", TargetType: "user", TargetId: user.Id.ToString(),
                Detail: new { from = before, to = name }));
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(ct);
    }
}
