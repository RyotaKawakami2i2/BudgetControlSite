using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QRCoder;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Infrastructure.Sessions;
using TaskYojitsu.Web.Security;

namespace TaskYojitsu.Web.Components.Account;

/// <summary>認証の画面の文言（詳細設計書 7.7 の MSG-AUT など）。</summary>
public static class AuthTexts
{
    public const string InvalidLogin = "メールアドレス、パスワード、認証コードのいずれかが正しくありません。";
    public const string Locked = "ログインに続けて失敗したため、アカウントを一時的にロックしました。15 分ほどたってからやり直してください。";
    public const string PasswordLength = "パスワードは 15 文字以上、128 文字以下にしてください。";
    public const string PasswordCommon = "このパスワードは、よく使われているか、過去に漏えいしたことがあるため使えません。";
    public const string PasswordPersonal = "メールアドレスや名前を含むパスワードは使えません。";
    public const string PasswordTrivial = "同じ文字や続いた文字だけのパスワードは使えません。";
    public const string LinkExpired = "リンクの有効期限が切れているか、すでに使われています。";
    public const string ResetRequested = "入力したメールアドレスが登録されていれば、再設定のメールを送りました。";
    public const string ReauthRequired = "続けるには、もう一度本人確認をしてください。";
    public const string RecoveryCodesLow = "リカバリーコードの残りが {0} 個です。新しいコードを発行してください。";
    public const string MfaRecommended = "多要素認証が設定されていません。アカウントを守るため、認証アプリかパスキーの設定をおすすめします。";
    public const string TooMany = "操作の回数が多すぎます。しばらく待ってからやり直してください。";
    public const string PasswordMismatch = "確認用のパスワードが一致しません。";
    public const string Required = "入力してください。";

    /// <summary>パスワードの検査の結果（Identity のエラーのコードにメッセージ ID が入っている）を文言にする。</summary>
    public static string ForPasswordErrors(IEnumerable<IdentityError> errors)
    {
        var lines = errors.Select(e => e.Code switch
        {
            "MSG-AUT-003" => PasswordLength,
            "MSG-AUT-004" => PasswordCommon,
            "MSG-AUT-005" => PasswordPersonal,
            "MSG-AUT-006" => PasswordTrivial,
            "InvalidToken" => LinkExpired,
            _ => "パスワードを設定できませんでした。",
        }).Distinct();
        return string.Join(" ", lines);
    }
}

/// <summary>画面の移動（自サイト内の URL だけを許す。NF-INP-08）。</summary>
public sealed class IdentityRedirectManager(NavigationManager navigationManager, CookieSettings cookies)
{
    private static readonly TimeSpan StatusMaxAge = TimeSpan.FromSeconds(10);

    /// <summary>戻り先として受け付けるのは、自サイト内のパスだけ。</summary>
    public static string SafeReturnUrl(string? returnUrl, string fallback = "/app")
    {
        if (string.IsNullOrEmpty(returnUrl)
            || !returnUrl.StartsWith('/')
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            || returnUrl.Contains('\\', StringComparison.Ordinal)
            || returnUrl.Any(char.IsControl))
        {
            return fallback;
        }

        return returnUrl;
    }

    public void RedirectTo(string uri) => navigationManager.NavigateTo(SafeReturnUrl(uri, "/account/login"));

    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(cookies.Status, message, cookies.Options(httpOnly: true, SameSiteMode.Strict, StatusMaxAge));
        RedirectTo(uri);
    }

    public string? TakeStatus(HttpContext context)
    {
        var message = context.Request.Cookies[cookies.Status];
        if (message is not null)
        {
            context.Response.Cookies.Delete(cookies.Status, cookies.Options(httpOnly: true, SameSiteMode.Strict));
        }

        return message;
    }
}

/// <summary>
/// 再認証の後に、セッション ID を作り直して Cookie を書き換える（NF-SES-02）。認証した時刻も更新する。
/// </summary>
public sealed class SessionRotation(DbTicketStore store, IOptionsMonitor<CookieAuthenticationOptions> cookieOptions)
{
    private const string SessionIdClaim = "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    public async Task<bool> RotateAsync(HttpContext context, AuthMethod? method)
    {
        if (SessionInfo.From(context) is not { } current)
        {
            return false;
        }

        var key = await store.RotateAsync(current, method, context, context.RequestAborted);
        if (key is null)
        {
            return false;
        }

        var options = cookieOptions.Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SessionIdClaim, key, ClaimValueTypes.String, options.ClaimsIssuer)], options.ClaimsIssuer));
        var ticket = new AuthenticationTicket(principal, null, IdentityConstants.ApplicationScheme);
        var cookie = options.Cookie.Build(context);
        cookie.Expires = null;
        options.CookieManager.AppendResponseCookie(context, options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), cookie);
        return true;
    }
}

/// <summary>ブラウザから受け取ったパスキーの資格情報（JSON）。</summary>
public static class PasskeyCredential
{
    /// <summary>資格情報の ID から利用者を探す（ログインの結果の記録に使う）。</summary>
    public static async Task<TaskYojitsu.Domain.Entities.User?> FindUserAsync(UserManager<TaskYojitsu.Domain.Entities.User> users, string credentialJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(credentialJson);
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetString() is { } value)
            {
                return await users.FindByPasskeyIdAsync(System.Buffers.Text.Base64Url.DecodeFromChars(value));
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            // 形式が正しくない資格情報
        }

        return null;
    }
}

/// <summary>認証アプリ登録用の QR コード。SVG を画面に直接埋め込む（画像の data URI や style 属性は使わない）。</summary>
public static class QrCodeSvg
{
    public static MarkupString Render(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (matrix[y][x])
                {
                    path.Append('M').Append(x).Append(' ').Append(y).Append("h1v1h-1z");
                }
            }
        }

        // 中身は QR コードの模様だけで、利用者の入力は含まない
        return new MarkupString(
            $"<svg class=\"qr\" xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" role=\"img\" aria-label=\"認証アプリ登録用の QR コード\" shape-rendering=\"crispEdges\">"
            + $"<rect width=\"{size}\" height=\"{size}\" fill=\"#ffffff\"/><path d=\"{path}\" fill=\"#000000\"/></svg>");
    }
}
