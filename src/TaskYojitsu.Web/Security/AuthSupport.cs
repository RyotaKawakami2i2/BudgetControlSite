using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Identity;
using TaskYojitsu.Infrastructure.Persistence;
using TaskYojitsu.Infrastructure.Sessions;

namespace TaskYojitsu.Web.Security;

/// <summary>
/// Cookie の名前（詳細設計書 8.2）。本番では __Host- の接頭辞と Secure を付ける。
/// 開発環境（http://localhost）で Secure を付けない設定にした場合だけ、接頭辞を外す（ブラウザが __Host- の Cookie を受け付けないため）。
/// </summary>
public sealed class CookieSettings
{
    public CookieSettings(IConfiguration config)
    {
        Secure = config.GetValue("Security:Cookies:Secure", true);
        var prefix = Secure ? "__Host-" : "";
        Session = $"{prefix}tyj.session";
        TwoFactor = $"{prefix}tyj.2fa";
        Antiforgery = $"{prefix}tyj.af";
        Xsrf = $"{prefix}tyj.xsrf";
        Flow = $"{prefix}tyj.flow";
        External = $"{prefix}tyj.ext";
        RememberMe = $"{prefix}tyj.2fa-rm";
        Status = $"{prefix}tyj.status";
    }

    public bool Secure { get; }
    public string Session { get; }
    public string TwoFactor { get; }
    public string Antiforgery { get; }
    public string Xsrf { get; }
    public string Flow { get; }
    public string External { get; }
    public string RememberMe { get; }
    public string Status { get; }

    public CookieSecurePolicy Policy => Secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

    public CookieOptions Options(bool httpOnly, SameSiteMode sameSite, TimeSpan? maxAge = null) => new()
    {
        Secure = Secure,
        HttpOnly = httpOnly,
        SameSite = sameSite,
        Path = "/",
        IsEssential = true,
        MaxAge = maxAge,
    };
}

/// <summary>SPA が読んで X-XSRF-TOKEN ヘッダーで返すトークンを渡す（詳細設計書 8.3）。</summary>
public static class XsrfCookie
{
    public static void Issue(HttpContext context)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var cookies = context.RequestServices.GetRequiredService<CookieSettings>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        if (tokens.RequestToken is { } token)
        {
            context.Response.Cookies.Append(cookies.Xsrf, token, cookies.Options(httpOnly: false, SameSiteMode.Strict));
        }
    }

    /// <summary>ログインのたびにトークンを作り直す（古い Cookie を消す）。</summary>
    public static void Reset(HttpContext context)
    {
        var cookies = context.RequestServices.GetRequiredService<CookieSettings>();
        context.Response.Cookies.Delete(cookies.Antiforgery, cookies.Options(httpOnly: true, SameSiteMode.Strict));
        context.Response.Cookies.Delete(cookies.Xsrf, cookies.Options(httpOnly: false, SameSiteMode.Strict));
    }
}

/// <summary>再認証の確認（直近 10 分以内に認証しているか。NF-AUT-06、詳細設計書 6.7）。</summary>
public sealed class ReauthGuard(IOptions<SecurityOptions> options, TimeProvider time)
{
    public bool IsRecent(HttpContext context) =>
        SessionInfo.From(context) is { } session
        && time.GetUtcNow().UtcDateTime - session.AuthTime <= TimeSpan.FromMinutes(options.Value.Reauth.Minutes);

    public static string RedirectPath(HttpContext context) =>
        "/account/reauthenticate?returnUrl=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
}

/// <summary>
/// ログインの前後の処理（監査ログ、最終ログイン日時、新しい端末の検知とメール、ロックのメール）。
/// </summary>
public sealed partial class LoginEvents(
    AppDbContext db,
    IAuditWriter audit,
    Mailer mailer,
    LoginHintHasher hintHasher,
    BusinessClock clock,
    IOptions<SecurityOptions> options,
    ILogger<LoginEvents> logger)
{
    public async Task OnSucceededAsync(HttpContext context, User user, AuthMethod method, bool notifyNewDevice = true)
    {
        var now = clock.UtcNow;
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now));
        await audit.WriteNowAsync(new AuditEntry("auth.login", TargetType: "user", TargetId: user.Id.ToString(), ActorId: user.Id,
            Detail: new { method = method.ToCode() }));
        if (method == AuthMethod.PasswordRecovery)
        {
            await audit.WriteNowAsync(new AuditEntry("auth.recovery_code.used", TargetType: "user", TargetId: user.Id.ToString(), ActorId: user.Id));
        }

        XsrfCookie.Reset(context);

        // 新しい端末からのログインを本人に知らせる（FR-NTF-04。招待を受諾した直後は知らせない）
        var ip = ClientIp.Of(context);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var fingerprint = Fingerprint(userAgent, ip);
        var since = now.AddDays(-options.Value.NewDeviceLookbackDays);
        var known = await db.UserKnownDevices.SingleOrDefaultAsync(d => d.UserId == user.Id && d.Fingerprint == fingerprint);
        var previouslySeen = known?.LastSeenAt;
        if (known is null)
        {
            db.UserKnownDevices.Add(new UserKnownDevice { UserId = user.Id, Fingerprint = fingerprint, FirstSeenAt = now, LastSeenAt = now });
        }
        else
        {
            known.LastSeenAt = now;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "端末の記録を保存できませんでした");
        }

        if (notifyNewDevice && (previouslySeen is null || previouslySeen < since))
        {
            await mailer.SendAsync(user.Email, EmailTemplates.NewDevice(clock.ToLocal(now), Browser(userAgent), ip?.ToString() ?? ""));
        }
    }

    /// <summary>ログインの失敗。入力されたメールアドレスは平文で残さず、HMAC の値を残す。</summary>
    public Task OnFailedAsync(string? email, Guid? userId, string reason) =>
        audit.WriteNowAsync(new AuditEntry("auth.login", AuditResult.Failure, "user", userId?.ToString(),
            ActorHint: string.IsNullOrEmpty(email) ? null : hintHasher.Hash(email), Detail: new { reason }));

    /// <summary>この試行でロックした場合に、記録して本人にメールで知らせる。</summary>
    public async Task OnLockedAsync(User user)
    {
        await audit.WriteNowAsync(new AuditEntry("auth.login.locked", AuditResult.Failure, "user", user.Id.ToString()));
        await mailer.SendAsync(user.Email, EmailTemplates.Locked(clock.ToLocal(clock.UtcNow), options.Value.Lockout.Minutes));
    }

    /// <summary>端末の指紋（ブラウザの種類とメジャーバージョン + IP アドレスの上位 24 ビット。IPv6 は上位 48 ビット）。</summary>
    public static byte[] Fingerprint(string userAgent, IPAddress? ip)
    {
        var prefix = "";
        if (ip is not null)
        {
            var bytes = ip.GetAddressBytes();
            prefix = ip.AddressFamily == AddressFamily.InterNetworkV6
                ? Convert.ToHexString(bytes, 0, 6)
                : string.Join('.', bytes.Take(3));
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes($"{Browser(userAgent)}|{prefix}"));
    }

    /// <summary>ブラウザの種類とメジャーバージョン（例: Chrome 141）。</summary>
    public static string Browser(string userAgent)
    {
        foreach (var (name, pattern) in BrowserPatterns)
        {
            var match = pattern.Match(userAgent);
            if (match.Success)
            {
                return $"{name} {match.Groups[1].Value}";
            }
        }

        return "不明なブラウザ";
    }

    private static readonly (string Name, Regex Pattern)[] BrowserPatterns =
    [
        ("Edge", EdgePattern()),
        ("Firefox", FirefoxPattern()),
        ("Chrome", ChromePattern()),
        ("Safari", SafariPattern()),
    ];

    [GeneratedRegex(@"Edg/(\d+)")]
    private static partial Regex EdgePattern();

    [GeneratedRegex(@"Firefox/(\d+)")]
    private static partial Regex FirefoxPattern();

    [GeneratedRegex(@"Chrome/(\d+)")]
    private static partial Regex ChromePattern();

    [GeneratedRegex(@"Version/(\d+).*Safari/")]
    private static partial Regex SafariPattern();
}

/// <summary>
/// 招待・再設定のトークンを、暗号化した短期間の Cookie に移す（詳細設計書 6.8）。トークンがブラウザの履歴や画面の URL に残らないようにするため。
/// </summary>
public sealed class FlowTokenCookie(Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtection, CookieSettings cookies)
{
    private readonly Microsoft.AspNetCore.DataProtection.ITimeLimitedDataProtector _protector =
        Microsoft.AspNetCore.DataProtection.DataProtectionAdvancedExtensions.ToTimeLimitedDataProtector(
            dataProtection.CreateProtector("TaskYojitsu.FlowToken.v1"));

    public void Store(HttpContext context, string purpose, string token) =>
        context.Response.Cookies.Append(cookies.Flow, _protector.Protect($"{purpose}|{token}", TimeSpan.FromMinutes(10)),
            cookies.Options(httpOnly: true, SameSiteMode.Strict, TimeSpan.FromMinutes(10)));

    public string? Read(HttpContext context, string purpose)
    {
        var value = context.Request.Cookies[cookies.Flow];
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            var plain = _protector.Unprotect(value);
            var parts = plain.Split('|', 2);
            return parts.Length == 2 && parts[0] == purpose ? parts[1] : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(cookies.Flow, cookies.Options(httpOnly: true, SameSiteMode.Strict));
}
