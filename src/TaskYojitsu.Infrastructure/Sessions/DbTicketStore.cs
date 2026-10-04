using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Infrastructure.Sessions;

/// <summary>要求に結び付いたセッションの情報（HttpContext.Items に入れる）。</summary>
public sealed record SessionInfo(
    Guid Id,
    Guid UserId,
    AuthMethod AuthMethod,
    DateTime AuthTime,
    DateTime CreatedAt,
    DateTime LastSeenAt,
    DateTime ExpiresAt,
    string Key)
{
    public const string ItemKey = "tyj.session";

    public static SessionInfo? From(HttpContext context) => context.Items[ItemKey] as SessionInfo;
}

/// <summary>
/// Cookie 認証のセッションを DB（user_sessions）に保存する（詳細設計書 6.6、NF-SES-01〜05）。
/// Cookie にはランダムな鍵だけを入れ（データ保護で暗号化される）、DB には鍵の SHA-256 の値と、暗号化した認証情報を保存する。
/// 要求のたびに、失効、作成から 12 時間、最後の操作から 30 分、利用者が有効か、セキュリティスタンプを確かめる。
/// </summary>
public sealed class DbTicketStore(
    IDbContextFactory<AppDbContext> factory,
    IDataProtectionProvider dataProtection,
    TimeProvider time,
    IOptionsMonitor<SecurityOptions> options,
    IOptionsMonitor<IdentityOptions> identityOptions,
    ILogger<DbTicketStore> logger) : ITicketStore
{
    /// <summary>サインインに使った認証の方式を入れるクレーム（AppSignInManager が付ける）。</summary>
    public const string AuthMethodClaim = "tyj:amth";

    /// <summary>画面が自動で送る要求（セッションを延長しない。詳細設計書 6.6）。</summary>
    private static readonly string[] AutomaticPaths = ["/api/v1/session/status", "/api/v1/notifications/unread-count"];

    /// <summary>
    /// サインインの要求。ここでは既存のセッションを使わず、ログインのたびにセッション ID を作り直す（NF-SES-02）。
    /// </summary>
    private static readonly string[] SignInPaths =
        ["/account/login", "/account/login-2fa", "/account/login-recovery", "/account/accept-invitation"];

    private readonly IDataProtector _protector = dataProtection.CreateProtector("TaskYojitsu.SessionTicket.v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new NotSupportedException();

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => throw new NotSupportedException();

    public Task RemoveAsync(string key) => throw new NotSupportedException();

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("利用者の ID がありません。"));
        var method = EnumCodes.TryParse<AuthMethod>(ticket.Principal.FindFirstValue(AuthMethodClaim), out var m) ? m : AuthMethod.Password;
        var settings = options.CurrentValue.Session;
        var now = time.GetUtcNow().UtcDateTime;
        var key = SecureTokens.Create();
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            KeyHash = SecureTokens.Hash(key),
            Ticket = Protect(ticket),
            AuthMethod = method,
            AuthTime = now,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddHours(settings.AbsoluteHours),
            Ip = httpContext.Connection.RemoteIpAddress ?? IPAddress.None,
            UserAgent = Truncate(httpContext.Request.Headers.UserAgent.ToString(), 256),
        };

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        httpContext.Items[SessionInfo.ItemKey] = ToInfo(session, key);
        return key;
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var hash = SecureTokens.Hash(key);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.UserSessions.AsNoTracking()
            .Where(s => s.KeyHash == hash)
            .Join(db.Users, s => s.UserId, u => u.Id, (s, u) => new { Session = s, u.Status, u.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var session = row.Session;
        var now = time.GetUtcNow().UtcDateTime;
        var settings = options.CurrentValue.Session;

        if (IsSignInRequest(httpContext.Request))
        {
            // ログインし直す要求では、古いセッションを使わず失効させる
            await RevokeAsync(db, session.Id, SessionRevokeReason.Logout, now, cancellationToken);
            return null;
        }

        if (session.RevokedAt is not null
            || session.ExpiresAt <= now
            || now - session.LastSeenAt > TimeSpan.FromMinutes(settings.IdleMinutes)
            || row.Status != UserStatus.Active)
        {
            return null;
        }

        AuthenticationTicket? ticket;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            logger.LogWarning(ex, "セッションの認証情報を復号できませんでした");
            return null;
        }

        // パスワードの変更などでセキュリティスタンプが変わったら、そのセッションは使えない（NF-AUT-07）
        var stampClaim = identityOptions.CurrentValue.ClaimsIdentity.SecurityStampClaimType;
        if (ticket is null || ticket.Principal.FindFirstValue(stampClaim) != row.SecurityStamp)
        {
            return null;
        }

        var lastSeen = session.LastSeenAt;
        if (!IsAutomatic(httpContext.Request) && now - session.LastSeenAt >= TimeSpan.FromSeconds(settings.TouchIntervalSeconds))
        {
            lastSeen = now;
            await db.UserSessions.Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), cancellationToken);
        }

        httpContext.Items[SessionInfo.ItemKey] = ToInfo(session, key) with { LastSeenAt = lastSeen };
        return ticket;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var hash = SecureTokens.Hash(key);
        var data = Protect(ticket);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await db.UserSessions.Where(s => s.KeyHash == hash && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Ticket, data), cancellationToken);
    }

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var hash = SecureTokens.Hash(key);
        var now = time.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var id = await db.UserSessions.Where(s => s.KeyHash == hash).Select(s => (Guid?)s.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is { } sessionId)
        {
            await RevokeAsync(db, sessionId, SessionRevokeReason.Logout, now, cancellationToken);
        }
    }

    /// <summary>
    /// 再認証の後に、新しい鍵のセッションに移す（セッション ID を作り直し、認証した時刻を更新する）。新しい鍵を返す。
    /// </summary>
    public async Task<string?> RotateAsync(SessionInfo current, AuthMethod? method, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var old = await db.UserSessions.SingleOrDefaultAsync(s => s.Id == current.Id && s.RevokedAt == null, cancellationToken);
        if (old is null)
        {
            return null;
        }

        var key = SecureTokens.Create();
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = old.UserId,
            KeyHash = SecureTokens.Hash(key),
            Ticket = old.Ticket,
            AuthMethod = method ?? old.AuthMethod,
            AuthTime = now,
            CreatedAt = old.CreatedAt,
            LastSeenAt = now,
            ExpiresAt = old.ExpiresAt,
            Ip = httpContext.Connection.RemoteIpAddress ?? IPAddress.None,
            UserAgent = Truncate(httpContext.Request.Headers.UserAgent.ToString(), 256),
        };
        old.RevokedAt = now;
        old.RevokedReason = SessionRevokeReason.Logout;
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        httpContext.Items[SessionInfo.ItemKey] = ToInfo(session, key);
        return key;
    }

    private static bool IsAutomatic(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) && AutomaticPaths.Any(p => request.Path.Equals(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsSignInRequest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && SignInPaths.Any(p => request.Path.Equals(p, StringComparison.OrdinalIgnoreCase));

    private static async Task RevokeAsync(AppDbContext db, Guid sessionId, SessionRevokeReason reason, DateTime now, CancellationToken ct) =>
        await db.UserSessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, reason), ct);

    private byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static SessionInfo ToInfo(UserSession s, string key) =>
        new(s.Id, s.UserId, s.AuthMethod, s.AuthTime, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, key);

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
