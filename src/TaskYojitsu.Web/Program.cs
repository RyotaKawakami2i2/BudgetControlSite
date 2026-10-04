using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure;
using TaskYojitsu.Infrastructure.Identity;
using TaskYojitsu.Infrastructure.Persistence;
using TaskYojitsu.Infrastructure.Sessions;
using TaskYojitsu.Web.Components;
using TaskYojitsu.Web.Components.Account;
using TaskYojitsu.Web.Endpoints;
using TaskYojitsu.Web.Jobs;
using TaskYojitsu.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// 秘密情報は Docker の secrets（ファイル）から読む。設定ファイルには書かない（NF-CRY-03）
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 64 * 1024;
});

var config = builder.Configuration;
var isDevelopment = builder.Environment.IsDevelopment();

// ---------------------------------------------------------------- 設定値（詳細設計書 11章）
builder.Services.Configure<AppOptions>(config.GetSection(AppOptions.Section));
builder.Services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
builder.Services.Configure<BusinessOptions>(o =>
{
    o.GanttMaxTasksPerResponse = config.GetValue("Gantt:MaxTasksPerResponse", 5000);
    o.SoftDeleteRetentionDays = config.GetValue("Tasks:SoftDeleteRetentionDays", 365);
    o.RestoreWindowDays = config.GetValue("Tasks:RestoreWindowDays", 30);
    o.NotificationRetentionDays = config.GetValue("Notifications:RetentionDays", 180);
    o.ProgressLagThreshold = config.GetValue("Delay:ProgressLagThreshold", 20);
    o.AuditRetentionYears = config.GetValue("Audit:RetentionYears", 3);
});
builder.Services.TryAddSingleton(TimeProvider.System);

// ---------------------------------------------------------------- 層ごとの部品
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config, isDevelopment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CookieSettings>();
builder.Services.AddSingleton<AdminNetworks>();
builder.Services.AddScoped<TaskYojitsu.Application.Abstractions.IRequestContext, HttpRequestContext>();
builder.Services.AddScoped<ApiRequestGuard>();
builder.Services.AddScoped<AdminAccessFilter>();
builder.Services.AddScoped<LoginEvents>();
builder.Services.AddSingleton<ReauthGuard>();
builder.Services.AddSingleton<AuthRateLimiter>();
builder.Services.AddSingleton<FlowTokenCookie>();
builder.Services.AddScoped<SessionRotation>();
builder.Services.AddScoped<SecurityChanges>();

// ---------------------------------------------------------------- 認証（ASP.NET Core Identity。詳細設計書 6章）
builder.Services.AddAppIdentityCore(config).AddSignInManager<AppSignInManager>();
builder.Services.AddScoped(sp => (AppSignInManager)sp.GetRequiredService<SignInManager<User>>());
builder.Services.Configure<IdentityPasskeyOptions>(o =>
{
    o.ServerDomain = new Uri(config["App:BaseUrl"] ?? "http://localhost:5080").Host;
    o.UserVerificationRequirement = "required";
    o.ResidentKeyRequirement = "required";
    o.AttestationConveyancePreference = "none";
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromHours(12));

var cookieSettings = new CookieSettings(config);
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = IdentityConstants.ApplicationScheme;
        o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies(cookies =>
    {
        cookies.ApplicationCookie!.Configure(o =>
        {
            o.Cookie.Name = cookieSettings.Session;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = cookieSettings.Policy;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.Path = "/";
            o.Cookie.IsEssential = true;
            o.ExpireTimeSpan = TimeSpan.FromHours(config.GetValue("Security:Session:AbsoluteHours", 12));
            o.SlidingExpiration = false;
            o.LoginPath = "/account/login";
            o.LogoutPath = "/account/logout";
            o.AccessDeniedPath = "/account/access-denied";
            o.ReturnUrlParameter = "returnUrl";
            o.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    // 未ログインは auth.unauthenticated、Cookie はあるがセッションが無効なら auth.session_expired
                    var expired = context.Request.Cookies.ContainsKey(cookieSettings.Session);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Problems.Create(context.HttpContext, 401, expired ? "auth.session_expired" : "auth.unauthenticated",
                        "ログインしてください。").ExecuteAsync(context.HttpContext);
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    return Problems.Create(context.HttpContext, 403, "auth.forbidden", "この操作を行う権限がありません。")
                        .ExecuteAsync(context.HttpContext);
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
        cookies.TwoFactorUserIdCookie!.Configure(o =>
        {
            o.Cookie.Name = cookieSettings.TwoFactor;
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = cookieSettings.Policy;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });
        cookies.ExternalCookie!.Configure(o =>
        {
            o.Cookie.Name = cookieSettings.External;
            o.Cookie.SecurePolicy = cookieSettings.Policy;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });
        cookies.TwoFactorRememberMeCookie!.Configure(o =>
        {
            o.Cookie.Name = cookieSettings.RememberMe;
            o.Cookie.SecurePolicy = cookieSettings.Policy;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });
    });
builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
    .Configure<DbTicketStore>((o, store) => o.SessionStore = store);

// 初期状態ですべて拒否し、必要なものだけ許可する（NF-ACC-01）
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = cookieSettings.Antiforgery;
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = cookieSettings.Policy;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.Path = "/";
    o.HeaderName = "X-XSRF-TOKEN";
    o.SuppressXFrameOptionsHeader = true;
});

// ---------------------------------------------------------------- API の入出力
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------- 回数制限（詳細設計書 6.9）
builder.Services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.Section));
var rateLimits = config.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await Problems.Create(context.HttpContext, 429, "rate.limited", "操作の回数が多すぎます。").ExecuteAsync(context.HttpContext);
    };
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(http => http.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter($"user:{http.User.Identity?.Name ?? ClientIp.Of(http)?.ToString()}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.ApiByUser.Permits, Window = rateLimits.ApiByUser.Window })
            : RateLimitPartition.GetNoLimiter("none")),
        PartitionedRateLimiter.Create<HttpContext, string>(http => http.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetFixedWindowLimiter($"ip:{ClientIp.Of(http)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.ApiByIp.Permits, Window = rateLimits.ApiByIp.Window })
            : RateLimitPartition.GetNoLimiter("none")),
        PartitionedRateLimiter.Create<HttpContext, string>(http => http.Request.Path.StartsWithSegments("/api/v1/admin")
            ? RateLimitPartition.GetFixedWindowLimiter($"admin:{http.User.Identity?.Name ?? ClientIp.Of(http)?.ToString()}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = rateLimits.AdminApiByUser.Permits, Window = rateLimits.AdminApiByUser.Window })
            : RateLimitPartition.GetNoLimiter("none")));
});

// ---------------------------------------------------------------- 送信元（nginx から来た要求に限って X-Forwarded-For を使う）
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    foreach (var proxy in config.GetSection("Security:KnownProxies").Get<string[]>() ?? [])
    {
        o.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

// ---------------------------------------------------------------- 認証の画面（Blazor の静的 SSR）と定期処理
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddHostedService<JobScheduler>();
builder.Services.AddJobs();

var app = builder.Build();

// ---------------------------------------------------------------- 要求の処理の順序
app.UseForwardedHeaders();
app.UseMiddleware<RequestIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // 利用者に内部の情報（例外の内容、SQL、スタックトレース）を見せない（NF-LOG-07）
    ExceptionHandler = async http =>
    {
        var feature = http.Features.Get<IExceptionHandlerFeature>();
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
        if (feature?.Error is TaskYojitsu.Application.Common.AppException appException)
        {
            await Problems.FromException(http, appException).ExecuteAsync(http);
            return;
        }

        logger.LogError(feature?.Error, "想定外のエラー（{RequestId}）", RequestIdMiddleware.Get(http));
        if (http.Request.Path.StartsWithSegments("/api"))
        {
            await Problems.Create(http, 500, "server.error", "エラーが発生しました。").ExecuteAsync(http);
        }
        else
        {
            http.Response.Redirect($"/error?id={Uri.EscapeDataString(RequestIdMiddleware.Get(http) ?? "")}");
        }
    },
});
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAppShell(app.Environment);
app.MapApi();
app.MapAccountEndpoints();
app.MapRazorComponents<App>();

app.Run();

/// <summary>結合テスト（WebApplicationFactory）から参照するため。</summary>
public partial class Program;
