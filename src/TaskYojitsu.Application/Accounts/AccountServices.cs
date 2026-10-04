using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Application.Accounts;

/// <summary>セッションの失効（NF-AUT-07、NF-SES-04）。</summary>
public sealed class SessionRevoker(IAppDbContext db, BusinessClock clock)
{
    public Task<int> RevokeAllAsync(Guid userId, SessionRevokeReason reason, Guid? exceptSessionId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && (exceptSessionId == null || s.Id != exceptSessionId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }

    public Task<int> RevokeAsync(Guid userId, Guid sessionId, SessionRevokeReason reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return db.UserSessions
            .Where(s => s.UserId == userId && s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);
    }
}

/// <summary>メールを送る。失敗しても業務の処理は止めず、ログに残す。</summary>
public sealed class Mailer(IEmailSender sender, ILogger<Mailer> logger)
{
    public async Task SendAsync(string? to, (string Subject, string Body) message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(to))
        {
            return;
        }

        try
        {
            await sender.SendAsync(to, message.Subject, message.Body, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "メールを送れませんでした（件名: {Subject}）", message.Subject);
        }
    }
}

/// <summary>招待（FR-AUT-03、NF-AUT-05）。トークンは 256 ビットの乱数で、ハッシュ値だけを保存する。</summary>
public sealed class InvitationService(
    IAppDbContext db,
    UserManager<User> users,
    Mailer mailer,
    IAuditWriter audit,
    BusinessClock clock,
    IOptions<AppOptions> app,
    IOptions<SecurityOptions> security)
{
    /// <summary>新しい招待のトークンを作る（古いものは無効にする）。SaveChanges は呼び出し側で行う。</summary>
    public async Task<string> IssueAsync(Guid userId, Guid? createdBy, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.Invitations
            .Where(i => i.UserId == userId && i.UsedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, now), ct);
        var token = SecureTokens.Create();
        db.Invitations.Add(new Invitation
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = SecureTokens.Hash(token),
            ExpiresAt = now.AddHours(security.Value.Invitation.ValidHours),
            CreatedAt = now,
            CreatedBy = createdBy,
        });
        return token;
    }

    public string LinkFor(string token) => $"{app.Value.BaseUrl.TrimEnd('/')}/account/accept-invitation?token={token}";

    public Task SendAsync(User user, string token, CancellationToken ct) =>
        mailer.SendAsync(user.Email, EmailTemplates.Invitation(user.DisplayName, LinkFor(token), security.Value.Invitation.ValidHours), ct);

    /// <summary>有効な（期限内・未使用・取り消されていない）招待の利用者。</summary>
    public async Task<User?> FindUserByTokenAsync(string? token, CancellationToken ct)
    {
        if (!SecureTokens.LooksValid(token))
        {
            return null;
        }

        var hash = SecureTokens.Hash(token!);
        var now = clock.UtcNow;
        var invitation = await db.Invitations.AsNoTracking()
            .SingleOrDefaultAsync(i => i.TokenHash == hash && i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct);
        if (invitation is null)
        {
            return null;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == invitation.UserId, ct);
        return user is { Status: UserStatus.Invited } ? user : null;
    }

    /// <summary>招待を受諾してパスワードを設定する。トークンは 1 回だけ使える。</summary>
    public async Task<(User? User, IdentityResult Result)> AcceptAsync(string token, string password, CancellationToken ct)
    {
        var found = await FindUserByTokenAsync(token, ct);
        if (found is null)
        {
            return (null, IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hash = SecureTokens.Hash(token);
        var now = clock.UtcNow;
        var claimed = await db.Invitations
            .Where(i => i.TokenHash == hash && i.UsedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAt, now), ct);
        if (claimed != 1)
        {
            return (null, IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));
        }

        var user = await users.FindByIdAsync(found.Id.ToString());
        if (user is null)
        {
            return (null, IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));
        }

        var result = await users.AddPasswordAsync(user, password);
        if (!result.Succeeded)
        {
            // パスワードが規則に合わなければ、トークンは使用済みにしない
            await tx.RollbackAsync(ct);
            return (user, result);
        }

        user.Status = UserStatus.Active;
        user.EmailConfirmed = true;
        user.UpdatedAt = now;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return (user, update);
        }

        audit.Add(new AuditEntry("user.activated", TargetType: "user", TargetId: user.Id.ToString(), ActorId: user.Id));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (user, IdentityResult.Success);
    }
}

/// <summary>パスワードの再設定（FR-AUT-04、NF-AUT-04、05）。</summary>
public sealed class PasswordResetService(
    IAppDbContext db,
    UserManager<User> users,
    SessionRevoker sessions,
    Mailer mailer,
    IAuditWriter audit,
    BusinessClock clock,
    IOptions<AppOptions> app,
    IOptions<SecurityOptions> security)
{
    /// <summary>
    /// 再設定の申請。登録の有無にかかわらず、呼び出し側は同じ画面を出す。登録がない人・無効な人にはメールを送らない。
    /// </summary>
    public async Task RequestAsync(string? email, System.Net.IPAddress? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return;
        }

        var normalized = users.NormalizeEmail(email.Trim());
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalized && u.Status == UserStatus.Active, ct);
        if (user is null)
        {
            return;
        }

        var now = clock.UtcNow;
        await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        var token = SecureTokens.Create();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = SecureTokens.Hash(token),
            ExpiresAt = now.AddMinutes(security.Value.PasswordReset.ValidMinutes),
            CreatedAt = now,
            RequestIp = ip,
        });
        audit.Add(new AuditEntry("auth.password.reset_requested", TargetType: "user", TargetId: user.Id.ToString()));
        await db.SaveChangesAsync(ct);

        var link = $"{app.Value.BaseUrl.TrimEnd('/')}/account/reset-password?token={token}";
        await mailer.SendAsync(user.Email, EmailTemplates.PasswordReset(link, security.Value.PasswordReset.ValidMinutes), ct);
    }

    public async Task<User?> FindUserByTokenAsync(string? token, CancellationToken ct)
    {
        if (!SecureTokens.LooksValid(token))
        {
            return null;
        }

        var hash = SecureTokens.Hash(token!);
        var now = clock.UtcNow;
        var row = await db.PasswordResetTokens.AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now, ct);
        if (row is null)
        {
            return null;
        }

        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == row.UserId && u.Status == UserStatus.Active, ct);
    }

    /// <summary>パスワードを変え、全セッションを失効させ、本人にメールで知らせる。</summary>
    public async Task<IdentityResult> ResetAsync(string token, string newPassword, CancellationToken ct)
    {
        var found = await FindUserByTokenAsync(token, ct);
        if (found is null)
        {
            return IdentityResult.Failed(new IdentityError { Code = "InvalidToken" });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var hash = SecureTokens.Hash(token);
        var now = clock.UtcNow;
        var claimed = await db.PasswordResetTokens
            .Where(t => t.TokenHash == hash && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), ct);
        if (claimed != 1)
        {
            return IdentityResult.Failed(new IdentityError { Code = "InvalidToken" });
        }

        var user = await users.FindByIdAsync(found.Id.ToString());
        if (user is null)
        {
            return IdentityResult.Failed(new IdentityError { Code = "InvalidToken" });
        }

        // 新しいパスワードを先に検査する（規則に合わなければトークンは使用済みにしない）
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, user, newPassword);
            if (!check.Succeeded)
            {
                await tx.RollbackAsync(ct);
                return check;
            }
        }

        await users.RemovePasswordAsync(user);
        var result = await users.AddPasswordAsync(user, newPassword);
        if (!result.Succeeded)
        {
            await tx.RollbackAsync(ct);
            return result;
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await sessions.RevokeAllAsync(user.Id, SessionRevokeReason.PasswordChanged, null, ct);
        audit.Add(new AuditEntry("auth.password.reset_completed", TargetType: "user", TargetId: user.Id.ToString(), ActorId: user.Id));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await mailer.SendAsync(user.Email, EmailTemplates.PasswordChanged(clock.ToLocal(now)), ct);
        return IdentityResult.Success;
    }
}
