using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Teams;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Admin;

/// <summary>利用者の一覧の 1 件（API-53）。多要素認証とパスキーの設定状況を含む（NF-AUT-11）。</summary>
public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    UserStatus Status,
    bool IsAdmin,
    bool MfaEnabled,
    int PasskeyCount,
    int TeamCount,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime? InvitationExpiresAt,
    bool Locked);

public sealed record InviteRequest(string? Email, string? DisplayName, bool IsAdmin);

public sealed record SetAdminRequest(bool IsAdmin);

public sealed record ResetMfaRequest(string? Verification);

/// <summary>招待の結果。link は運用コマンド（create-admin）でだけ表示に使う。</summary>
public sealed record InviteResult(AdminUserDto User, string Token);

/// <summary>
/// 利用者の管理（FR-ADM-01〜03、FR-AUT-08）。呼び出す前に、管理者であることと直近の再認証を確かめる（AdminAccessFilter）。
/// </summary>
public sealed class AdminUserService(
    IAppDbContext db,
    UserManager<User> users,
    AccessPolicy access,
    InvitationService invitations,
    MembershipService memberships,
    SessionRevoker sessions,
    Mailer mailer,
    IAuditWriter audit,
    IDbLocks locks,
    BusinessClock clock)
{
    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(string? q, string? status, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        var keyword = TextRules.NormalizeSingleLine(q);
        if (keyword is not null)
        {
            var lower = keyword.ToLowerInvariant();
            query = query.Where(u => u.DisplayName.ToLower().Contains(lower) || (u.Email != null && u.Email.ToLower().Contains(lower)));
        }

        if (status is not null)
        {
            if (!EnumCodes.TryParse<UserStatus>(status, out var s))
            {
                throw ValidationException.For("status", Msg.CmnChoice);
            }

            query = query.Where(u => u.Status == s);
        }

        var now = clock.UtcNow;
        var rows = await query
            .OrderBy(u => u.DisplayName)
            .Take(500)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Status,
                u.IsAdmin,
                u.TwoFactorEnabled,
                u.LastLoginAt,
                u.CreatedAt,
                u.LockoutEnd,
                Passkeys = db.UserPasskeys.Count(p => p.UserId == u.Id),
                Teams = db.TeamMembers.Count(m => m.UserId == u.Id && m.RemovedAt == null),
                Invitation = db.Invitations
                    .Where(i => i.UserId == u.Id && i.UsedAt == null && i.RevokedAt == null)
                    .OrderByDescending(i => i.CreatedAt)
                    .Select(i => (DateTime?)i.ExpiresAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return [.. rows.Select(u => new AdminUserDto(
            u.Id, u.Email ?? "", u.DisplayName, u.Status, u.IsAdmin, u.TwoFactorEnabled, u.Passkeys, u.Teams, u.LastLoginAt,
            u.CreatedAt, u.Status == UserStatus.Invited ? u.Invitation : null, u.LockoutEnd is { } end && end > now))];
    }

    /// <summary>招待する（メールアドレスと表示名）。利用者を「招待中」で作り、招待メールを送る。</summary>
    public async Task<InviteResult> InviteAsync(InviteRequest request, CancellationToken ct, bool sendEmail = true)
    {
        var v = new Validation();
        var email = ValidateEmail(v, request.Email);
        var displayName = v.SingleLine("displayName", request.DisplayName, Limits.DisplayNameMax, required: true);
        v.ThrowIfAny();

        if (await users.FindByEmailAsync(email!) is not null)
        {
            throw ValidationException.For("email", Msg.AdmEmailDuplicate);
        }

        var actorId = access.UserIdOrNull;
        var now = clock.UtcNow;
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = displayName!,
            Status = UserStatus.Invited,
            IsAdmin = request.IsAdmin,
            EmailConfirmed = false,
            LockoutEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            throw ValidationException.For("email", created.Errors.Any(e => e.Code.Contains("Duplicate", StringComparison.Ordinal))
                ? Msg.AdmEmailDuplicate
                : Msg.CmnChoice);
        }

        var token = await invitations.IssueAsync(user.Id, actorId, ct);
        audit.Add(new AuditEntry("user.invited", TargetType: "user", TargetId: user.Id.ToString(), ActorId: actorId,
            Detail: new { isAdmin = request.IsAdmin }));
        if (request.IsAdmin)
        {
            audit.Add(new AuditEntry("user.admin.granted", TargetType: "user", TargetId: user.Id.ToString(), ActorId: actorId));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (sendEmail)
        {
            await invitations.SendAsync(user, token, ct);
        }

        return new InviteResult((await ListOneAsync(user.Id, ct))!, token);
    }

    public async Task<AdminUserDto> ResendInvitationAsync(Guid userId, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (user.Status != UserStatus.Invited)
        {
            throw new RuleViolationException(Msg.CmnChoice);
        }

        var token = await invitations.IssueAsync(user.Id, access.UserId, ct);
        audit.Add(new AuditEntry("user.invitation.resent", TargetType: "user", TargetId: userId.ToString()));
        await db.SaveChangesAsync(ct);
        await invitations.SendAsync(user, token, ct);
        return (await ListOneAsync(userId, ct))!;
    }

    /// <summary>招待の取り消し（利用者は無効にする）。</summary>
    public async Task<AdminUserDto> RevokeInvitationAsync(Guid userId, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (user.Status != UserStatus.Invited)
        {
            throw new RuleViolationException(Msg.CmnChoice);
        }

        var now = clock.UtcNow;
        await db.Invitations.Where(i => i.UserId == userId && i.UsedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), ct);
        user.Status = UserStatus.Disabled;
        user.DisabledAt = now;
        user.UpdatedAt = now;
        audit.Add(new AuditEntry("user.invitation.revoked", TargetType: "user", TargetId: userId.ToString()));
        await db.SaveChangesAsync(ct);
        return (await ListOneAsync(userId, ct))!;
    }

    /// <summary>
    /// 無効化（詳細設計書 4.9）。全セッションを失効させ、所属チームから外し、未使用の招待を無効にする。データは削除しない。
    /// </summary>
    public async Task<AdminUserDto> DisableAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockAsync("admins", ct);
        var user = await LoadAsync(userId, ct);
        if (user.Status == UserStatus.Disabled)
        {
            return (await ListOneAsync(userId, ct))!;
        }

        if (user.IsAdmin)
        {
            await EnsureNotLastAdminAsync(userId, ct);
        }

        var now = clock.UtcNow;
        user.Status = UserStatus.Disabled;
        user.DisabledAt = now;
        user.UpdatedAt = now;
        await users.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(userId, SessionRevokeReason.Disabled, null, ct);
        await db.Invitations.Where(i => i.UserId == userId && i.UsedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), ct);

        var rows = await db.TeamMembers.Where(m => m.UserId == userId && m.RemovedAt == null).ToListAsync(ct);
        foreach (var row in rows)
        {
            await memberships.RemoveMembershipAsync(row, access.UserId, "disabled", ct);
        }

        audit.Add(new AuditEntry("user.disabled", TargetType: "user", TargetId: userId.ToString(), Detail: new { removedFromTeams = rows.Count }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ListOneAsync(userId, ct))!;
    }

    /// <summary>再有効化。パスワードを設定済みなら有効に、未設定なら招待中に戻す（チームの所属は戻さない）。</summary>
    public async Task<AdminUserDto> EnableAsync(Guid userId, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (user.Status != UserStatus.Disabled)
        {
            return (await ListOneAsync(userId, ct))!;
        }

        user.Status = user.PasswordHash is null ? UserStatus.Invited : UserStatus.Active;
        user.DisabledAt = null;
        user.UpdatedAt = clock.UtcNow;
        await users.UpdateAsync(user);
        audit.Add(new AuditEntry("user.enabled", TargetType: "user", TargetId: userId.ToString()));
        await db.SaveChangesAsync(ct);
        return (await ListOneAsync(userId, ct))!;
    }

    /// <summary>管理者権限の付与・解除。最後の 1 人は解除できない。権限が変わった本人には、ログインし直してもらう（NF-SES-02）。</summary>
    public async Task<AdminUserDto> SetAdminAsync(Guid userId, SetAdminRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockAsync("admins", ct);
        var user = await LoadAsync(userId, ct);
        if (user.IsAdmin == request.IsAdmin)
        {
            return (await ListOneAsync(userId, ct))!;
        }

        if (!request.IsAdmin)
        {
            await EnsureNotLastAdminAsync(userId, ct);
        }

        user.IsAdmin = request.IsAdmin;
        user.UpdatedAt = clock.UtcNow;
        await users.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(userId, SessionRevokeReason.AdminRevoked, null, ct);
        audit.Add(new AuditEntry(request.IsAdmin ? "user.admin.granted" : "user.admin.revoked", TargetType: "user", TargetId: userId.ToString()));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ListOneAsync(userId, ct))!;
    }

    /// <summary>多要素認証のリセット（FR-AUT-08）。本人確認の方法の記録を必須にする。</summary>
    public async Task<AdminUserDto> ResetMfaAsync(Guid userId, ResetMfaRequest request, CancellationToken ct)
    {
        var v = new Validation();
        var verification = v.SingleLine("verification", request.Verification, 200, required: true);
        v.ThrowIfAny();

        var user = await LoadAsync(userId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        user.LastTotpStep = null;
        user.UpdatedAt = clock.UtcNow;
        await users.UpdateSecurityStampAsync(user);
        await sessions.RevokeAllAsync(userId, SessionRevokeReason.MfaChanged, null, ct);
        audit.Add(new AuditEntry("auth.mfa.reset", TargetType: "user", TargetId: userId.ToString(), Detail: new { verification }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await mailer.SendAsync(user.Email, EmailTemplates.LoginSettingsChanged(clock.ToLocal(clock.UtcNow), "管理者が多要素認証をリセットしました"), ct);
        return (await ListOneAsync(userId, ct))!;
    }

    private async Task<AdminUserDto?> ListOneAsync(Guid userId, CancellationToken ct)
    {
        var email = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(ct);
        return (await ListAsync(email, null, ct)).FirstOrDefault(u => u.Id == userId);
    }

    private async Task<User> LoadAsync(Guid userId, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();

    private async Task EnsureNotLastAdminAsync(Guid userId, CancellationToken ct)
    {
        var others = await db.Users.CountAsync(u => u.IsAdmin && u.Id != userId && u.Status == UserStatus.Active, ct);
        if (others == 0)
        {
            throw new RuleViolationException(Msg.AdmLastAdmin);
        }
    }

    internal static string? ValidateEmail(Validation v, string? input)
    {
        var email = TextRules.NormalizeSingleLine(input);
        if (email is null)
        {
            v.Add("email", Msg.CmnRequired);
            return null;
        }

        if (email.Length > Limits.EmailMax || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email
            || !email.Contains('@', StringComparison.Ordinal) || email.Any(char.IsWhiteSpace))
        {
            v.Add("email", Msg.CmnChoice);
            return null;
        }

        return email;
    }
}
