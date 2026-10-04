using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Web.Security;

/// <summary>認証まわりの回数制限の種類（詳細設計書 6.9）。</summary>
public enum AuthLimit
{
    LoginByIp,
    LoginByEmail,
    SecondFactorByIp,
    ResetRequestByIp,
    ResetRequestByEmail,
    TokenByIp,
    ReauthByUser,
}

/// <summary>回数制限の 1 つの規則（期間ごとの上限の回数）。</summary>
public sealed class RateLimitRule
{
    public int Permits { get; set; }

    public int WindowSeconds { get; set; }

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}

/// <summary>回数制限の設定（RateLimits。初期値は詳細設計書 6.9 の値）。</summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public RateLimitRule LoginByIp { get; set; } = new() { Permits = 10, WindowSeconds = 60 };
    public RateLimitRule LoginByEmail { get; set; } = new() { Permits = 10, WindowSeconds = 15 * 60 };
    public RateLimitRule SecondFactorByIp { get; set; } = new() { Permits = 10, WindowSeconds = 60 };
    public RateLimitRule ResetRequestByIp { get; set; } = new() { Permits = 5, WindowSeconds = 60 * 60 };
    public RateLimitRule ResetRequestByEmail { get; set; } = new() { Permits = 3, WindowSeconds = 60 * 60 };
    public RateLimitRule TokenByIp { get; set; } = new() { Permits = 20, WindowSeconds = 60 * 60 };
    public RateLimitRule ReauthByUser { get; set; } = new() { Permits = 5, WindowSeconds = 15 * 60 };
    public RateLimitRule ApiByUser { get; set; } = new() { Permits = 300, WindowSeconds = 60 };
    public RateLimitRule ApiByIp { get; set; } = new() { Permits = 600, WindowSeconds = 60 };
    public RateLimitRule AdminApiByUser { get; set; } = new() { Permits = 60, WindowSeconds = 60 };

    public RateLimitRule For(AuthLimit limit) => limit switch
    {
        AuthLimit.LoginByIp => LoginByIp,
        AuthLimit.LoginByEmail => LoginByEmail,
        AuthLimit.SecondFactorByIp => SecondFactorByIp,
        AuthLimit.ResetRequestByIp => ResetRequestByIp,
        AuthLimit.ResetRequestByEmail => ResetRequestByEmail,
        AuthLimit.TokenByIp => TokenByIp,
        AuthLimit.ReauthByUser => ReauthByUser,
        _ => throw new ArgumentOutOfRangeException(nameof(limit)),
    };
}

/// <summary>
/// 認証の画面の回数制限。キーが入力の値（メールアドレスなど）に依るため、画面の処理の中で確かめる。
/// サーバーが 1 台なので、数はメモリで持つ。
/// </summary>
public sealed class AuthRateLimiter : IDisposable
{
    private readonly Dictionary<AuthLimit, PartitionedRateLimiter<string>> _limiters;
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;

    public AuthRateLimiter(IMemoryCache cache, IServiceScopeFactory scopes, IOptions<RateLimitOptions> options)
    {
        _cache = cache;
        _scopes = scopes;
        _limiters = Enum.GetValues<AuthLimit>().ToDictionary(limit => limit, limit => Create(options.Value.For(limit)));
    }

    /// <summary>1 回分を使う。上限を超えていたら false（記録は同じ対象につき 1 分に 1 件まで）。</summary>
    public async Task<bool> TryAcquireAsync(AuthLimit limit, string key)
    {
        using var lease = _limiters[limit].AttemptAcquire(key.ToUpperInvariant());
        if (lease.IsAcquired)
        {
            return true;
        }

        var cacheKey = $"rate:{limit}:{key}";
        if (!_cache.TryGetValue(cacheKey, out _))
        {
            _cache.Set(cacheKey, true, TimeSpan.FromMinutes(1));
            await using var scope = _scopes.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            await audit.WriteNowAsync(new AuditEntry("security.rate_limited", AuditResult.Denied, Detail: new { limit = limit.ToString() }));
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }

    private static PartitionedRateLimiter<string> Create(RateLimitRule rule) =>
        PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rule.Permits,
            Window = rule.Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
}
