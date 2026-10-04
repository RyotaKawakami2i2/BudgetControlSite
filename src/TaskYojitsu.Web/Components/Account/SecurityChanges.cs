using Microsoft.AspNetCore.Components;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Sessions;
using TaskYojitsu.Web.Security;

namespace TaskYojitsu.Web.Components.Account;

/// <summary>
/// アカウントのセキュリティ設定を変えた後の処理（NF-AUT-07、FR-NTF-04）。
/// 現在のセッションは新しいセキュリティスタンプで続け、ほかのセッションはすべて失効させ、監査ログに残し、本人にメールで知らせる。
/// </summary>
public sealed class SecurityChanges(
    AppSignInManager signIn,
    SessionRevoker sessions,
    Mailer mailer,
    IAuditWriter audit,
    BusinessClock clock)
{
    public async Task AfterChangeAsync(HttpContext context, User user, string action, SessionRevokeReason reason, string? settingChange)
    {
        await signIn.RefreshSignInAsync(user);
        var revoked = await sessions.RevokeAllAsync(user.Id, reason, SessionInfo.From(context)?.Id, context.RequestAborted);
        await audit.WriteNowAsync(new AuditEntry(action, TargetType: "user", TargetId: user.Id.ToString(), Detail: new { revokedSessions = revoked }));
        var now = clock.ToLocal(clock.UtcNow);
        await mailer.SendAsync(user.Email, settingChange is null
            ? EmailTemplates.PasswordChanged(now)
            : EmailTemplates.LoginSettingsChanged(now, settingChange));
    }
}

/// <summary>アカウント設定の画面の共通部分。セキュリティ設定の変更には、直近 10 分以内の再認証を求める（詳細設計書 6.7）。</summary>
public abstract class ManagePageBase : ComponentBase
{
    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;

    [Inject]
    protected Microsoft.AspNetCore.Identity.UserManager<User> UserManager { get; set; } = default!;

    [Inject]
    protected IdentityRedirectManager RedirectManager { get; set; } = default!;

    [Inject]
    protected ReauthGuard Reauth { get; set; } = default!;

    protected User? CurrentUser { get; private set; }

    /// <summary>利用者を読み込む。requireReauth なら、直近の再認証がなければ再認証の画面へ移る（false を返す）。</summary>
    protected async Task<bool> LoadAsync(bool requireReauth)
    {
        CurrentUser = await UserManager.GetUserAsync(HttpContext.User);
        if (CurrentUser is null)
        {
            RedirectManager.RedirectTo("/account/login");
            return false;
        }

        if (requireReauth && !Reauth.IsRecent(HttpContext))
        {
            RedirectManager.RedirectTo(ReauthGuard.RedirectPath(HttpContext));
            return false;
        }

        return true;
    }
}
