using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TaskYojitsu.Web.Security;

/// <summary>要求ごとの ID（X-Request-Id）。監査ログとアプリのログに残す。</summary>
public sealed partial class RequestIdMiddleware(RequestDelegate next)
{
    private const string ItemKey = "tyj.request-id";

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex Acceptable();

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    public Task InvokeAsync(HttpContext context)
    {
        // nginx などが付けた ID は形式が正しい場合だけ引き継ぐ
        var incoming = context.Request.Headers["X-Request-Id"].ToString();
        var id = Acceptable().IsMatch(incoming) ? incoming : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Request-Id"] = id;
            return Task.CompletedTask;
        });
        return next(context);
    }
}

/// <summary>
/// セキュリティのヘッダー（詳細設計書 8.1）。CSP は自サイト以外からの読み込み、HTML 内のスクリプトとスタイル、eval、他サイトへの埋め込みを禁止する。
/// ログイン後の HTML と API の応答はキャッシュさせない。名前にハッシュの付いた静的ファイルは長期間キャッシュさせる。
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    private readonly string _csp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; "
        + "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'"
        + (environment.IsDevelopment() ? "" : "; upgrade-insecure-requests");

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path;
            if (!environment.IsDevelopment())
            {
                headers.StrictTransportSecurity = "max-age=31536000; includeSubDomains";
            }

            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), publickey-credentials-get=(self)";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            var contentType = context.Response.ContentType ?? "";
            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                headers.ContentSecurityPolicy = _csp;
                headers.XFrameOptions = "DENY";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
            }

            if (path.StartsWithSegments("/app/assets"))
            {
                // 名前に内容のハッシュが付いた画面のファイル
                if (context.Response.StatusCode == StatusCodes.Status200OK)
                {
                    headers.CacheControl = "public, max-age=31536000, immutable";
                }
            }
            else if (IsPublicStatic(path))
            {
                // 名前にハッシュが付かないファイルは、使うたびに更新の有無を確かめさせる（古いスクリプトを使い続けないように）
                headers.CacheControl = "no-cache";
            }
            else
            {
                headers.CacheControl = "no-store";
                headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }

    private static bool IsPublicStatic(PathString path) =>
        path.StartsWithSegments("/js") || path.StartsWithSegments("/favicon.svg") || path.Equals("/app/account.css");
}
