using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Sessions;
using TaskYojitsu.Web.Security;

namespace TaskYojitsu.Web.Endpoints;

/// <summary>認証の画面が使う補助の要求（ログアウト、パスキーのオプション）。フォームか X-XSRF-TOKEN で CSRF を確かめる。</summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/account");

        // ログアウト（FR-AUT-06、NF-SES-04）。サーバー側のセッションを破棄する
        account.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery, SignInManager<User> signIn, IAuditWriter audit) =>
        {
            if (!await IsValidAsync(http, antiforgery))
            {
                return Results.BadRequest();
            }

            var sessionId = SessionInfo.From(http)?.Id;
            await signIn.SignOutAsync();
            XsrfCookie.Reset(http);
            await audit.WriteNowAsync(new AuditEntry("auth.logout", TargetType: "session", TargetId: sessionId?.ToString()));
            return WantsJson(http) ? Results.NoContent() : Results.LocalRedirect("/account/login?loggedOut=1");
        }).RequireAuthorization();

        // すべての端末からのログアウト
        account.MapPost("/logout-all", async (HttpContext http, IAntiforgery antiforgery, SignInManager<User> signIn, SessionRevoker sessions, IAuditWriter audit) =>
        {
            if (!await IsValidAsync(http, antiforgery))
            {
                return Results.BadRequest();
            }

            if (Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var count = await sessions.RevokeAllAsync(userId, SessionRevokeReason.UserRevoked, null, http.RequestAborted);
                await audit.WriteNowAsync(new AuditEntry("auth.session.revoked", TargetType: "session", Detail: new { scope = "all", count }));
            }

            await signIn.SignOutAsync();
            XsrfCookie.Reset(http);
            return WantsJson(http) ? Results.NoContent() : Results.LocalRedirect("/account/login?loggedOut=1");
        }).RequireAuthorization();

        // パスキーの登録のオプション（登録には再認証が必要。詳細設計書 6.5）
        account.MapPost("/passkey-creation-options", async (HttpContext http, IAntiforgery antiforgery, UserManager<User> users,
            SignInManager<User> signIn, ReauthGuard reauth) =>
        {
            await antiforgery.ValidateRequestAsync(http);
            if (!reauth.IsRecent(http))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var user = await users.GetUserAsync(http.User);
            if (user is null)
            {
                return Results.NotFound();
            }

            var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
            {
                Id = user.Id.ToString(),
                Name = user.Email ?? user.Id.ToString(),
                DisplayName = user.DisplayName,
            });
            return Results.Content(options, "application/json");
        }).RequireAuthorization();

        // パスキーでのログインのオプション（利用者を特定しない形にする。アカウントの有無を区別しない）
        account.MapPost("/passkey-request-options", async (HttpContext http, IAntiforgery antiforgery, SignInManager<User> signIn) =>
        {
            await antiforgery.ValidateRequestAsync(http);
            var options = await signIn.MakePasskeyRequestOptionsAsync(null);
            return Results.Content(options, "application/json");
        }).AllowAnonymous();
    }

    private static async Task<bool> IsValidAsync(HttpContext http, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static bool WantsJson(HttpContext http) =>
        http.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
}
