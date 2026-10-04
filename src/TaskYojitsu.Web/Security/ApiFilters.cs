using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Infrastructure.Sessions;

namespace TaskYojitsu.Web.Security;

/// <summary>
/// API の要求の確認（NF-INP-02、詳細設計書 8.3）。状態を変える要求（POST、PUT、PATCH、DELETE）では、
/// CSRF のトークン（X-XSRF-TOKEN と Cookie の照合）、Origin（ない場合は Sec-Fetch-Site）、Content-Type: application/json を確かめる。
/// </summary>
public sealed class ApiRequestGuard(
    IAntiforgery antiforgery,
    IOptions<AppOptions> app,
    IAuditWriter audit,
    IMemoryCache cache,
    ILogger<ApiRequestGuard> logger) : IEndpointFilter
{
    private readonly string _origin = new Uri(app.Value.BaseUrl).GetLeftPart(UriPartial.Authority);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (IsStateChanging(http.Request.Method))
        {
            var reason = await CheckAsync(http);
            if (reason is not null)
            {
                logger.LogWarning("CSRF の確認に失敗しました（{Reason}）", reason);
                await AuditOncePerMinuteAsync(http, reason);
                return Problems.Create(http, StatusCodes.Status403Forbidden, "security.csrf", "画面を読み込み直してください。");
            }
        }

        return await next(context);
    }

    public static bool IsStateChanging(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private async Task<string?> CheckAsync(HttpContext http)
    {
        if (!http.Request.HasJsonContentType())
        {
            return "content-type";
        }

        var origin = http.Request.Headers.Origin.ToString();
        if (origin.Length > 0)
        {
            if (!string.Equals(origin, _origin, StringComparison.OrdinalIgnoreCase))
            {
                return "origin";
            }
        }
        else if (!string.Equals(http.Request.Headers["Sec-Fetch-Site"].ToString(), "same-origin", StringComparison.OrdinalIgnoreCase))
        {
            return "sec-fetch-site";
        }

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return "token";
        }

        return null;
    }

    private async Task AuditOncePerMinuteAsync(HttpContext http, string reason)
    {
        var key = $"csrf:{ClientIp.Of(http)}:{reason}";
        if (cache.TryGetValue(key, out _))
        {
            return;
        }

        cache.Set(key, true, TimeSpan.FromMinutes(1));
        await audit.WriteNowAsync(new AuditEntry("security.csrf_rejected", AuditResult.Denied, Detail: new { reason, path = http.Request.Path.Value }));
    }
}

/// <summary>
/// 管理 API の確認（詳細設計書 4.5 の判定の順序 2、6.7）。管理者であること、直近 10 分以内の認証、許可したネットワークをすべて満たすこと。
/// </summary>
public sealed class AdminAccessFilter(AccessPolicy access, IAuditWriter audit, IOptions<SecurityOptions> security) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var user = await access.GetUserAsync(http.RequestAborted);
        string? code = null;
        if (!user.IsActive || !user.IsAdmin)
        {
            code = "auth.forbidden";
        }
        else if (!access.AdminStepUp)
        {
            code = "auth.reauth_required";
        }
        else if (security.Value.Mfa.RequiredForAdmins && SessionInfo.From(http) is { AuthMethod: AuthMethod.Password })
        {
            // 設定で、管理者だけ多要素認証を必須にできる（初期値は無効）
            code = "auth.mfa_required";
        }

        if (code is not null)
        {
            await audit.WriteNowAsync(new AuditEntry("access.denied", AuditResult.Denied, "admin", http.Request.Path.Value,
                Detail: new { code }), http.RequestAborted);
            return Problems.Create(http, StatusCodes.Status403Forbidden, code, "この操作を行う権限がありません。");
        }

        return await next(context);
    }
}

/// <summary>業務処理の失敗（AppException）を Problem Details にする（詳細設計書 5.2）。</summary>
public sealed class AppExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (AppException ex)
        {
            return Problems.FromException(context.HttpContext, ex);
        }
    }
}

/// <summary>RFC 9457 の Problem Details。画面に出すメッセージの ID（errors）とエラーのコードを含める。</summary>
public static class Problems
{
    public static IResult Create(HttpContext http, int status, string code, string title, object? errors = null, object? latest = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["traceId"] = RequestIdMiddleware.Get(http),
        };
        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        if (latest is not null)
        {
            extensions["latest"] = latest;
        }

        return Results.Problem(
            title: title,
            statusCode: status,
            type: $"/problems/{code}",
            extensions: extensions);
    }

    public static IResult FromException(HttpContext http, AppException ex) => ex switch
    {
        ValidationException v => Create(http, v.Status, v.Code, "入力内容を確認してください。", v.Errors),
        RuleViolationException r => Create(http, r.Status, r.Code, "この操作はできません。",
            new Dictionary<string, string[]> { [""] = [.. r.MessageIds] }),
        ConflictException c => Create(http, c.Status, c.Code, "ほかの人が先に更新しました。", latest: c.Latest),
        _ => Create(http, ex.Status, ex.Code, ex.Message),
    };
}
