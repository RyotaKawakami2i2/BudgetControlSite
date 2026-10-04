using System.Net;
using System.Text.Json.Nodes;
using TaskYojitsu.Web.Tests.Infrastructure;

namespace TaskYojitsu.Web.Tests;

/// <summary>CSRF の対策（E2E-10、詳細設計書 8.2）と、HTTP のヘッダー・Cookie の属性（詳細設計書 13.3）。</summary>
public sealed class SecurityTests(AppFactory app) : IAsyncLifetime
{
    private const string Target = "/api/v1/notifications/read-all";
    private TestUser _user = null!;

    public async ValueTask InitializeAsync()
    {
        _user = await TestWorld.CreateUserAsync(app, "security");
        await _user.Client.LoginAsync(_user.Email, _user.Password);
    }

    public ValueTask DisposeAsync()
    {
        _user.Client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 正しいトークンとOriginがあれば受け付ける()
    {
        using var response = await _user.Client.PostAsync(Target);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task トークンがない要求は拒否する()
    {
        using var response = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r => r.Headers.Remove("X-XSRF-TOKEN"));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "security.csrf");
    }

    [Fact]
    public async Task ほかの利用者のトークンは使えない()
    {
        var other = await TestWorld.CreateUserAsync(app, "security-other");
        await other.Client.LoginAsync(other.Email, other.Password);
        var foreignToken = other.Client.Cookie(TestClient.XsrfCookie)!;

        using var response = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r =>
        {
            r.Headers.Remove("X-XSRF-TOKEN");
            r.Headers.Add("X-XSRF-TOKEN", foreignToken);
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "security.csrf");
    }

    [Fact]
    public async Task ほかのオリジンからの要求は拒否する()
    {
        using var response = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r =>
        {
            r.Headers.Remove("Origin");
            r.Headers.Add("Origin", "https://evil.example");
        });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "security.csrf");
    }

    [Fact]
    public async Task Originがなければ同じサイトからの要求であることを求める()
    {
        using var noOrigin = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r => r.Headers.Remove("Origin"));
        using var sameSite = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r =>
        {
            r.Headers.Remove("Origin");
            r.Headers.Add("Sec-Fetch-Site", "same-origin");
        });

        await AssertProblemAsync(noOrigin, HttpStatusCode.Forbidden, "security.csrf");
        Assert.Equal(HttpStatusCode.OK, sameSite.StatusCode);
    }

    [Fact]
    public async Task JSON以外の本文は拒否する()
    {
        using var response = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r =>
            r.Content = new StringContent("x", System.Text.Encoding.UTF8, "text/plain"));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "security.csrf");
    }

    [Fact]
    public async Task CSRFの拒否を監査ログに残す()
    {
        var before = await app.Database.ScalarAsync<long>(
            "SELECT count(*) FROM tyj.audit_logs WHERE action = 'security.csrf_rejected' AND actor_id = $1", _user.Id);
        using var response = await _user.Client.SendAsync(HttpMethod.Post, Target, configure: r => r.Headers.Remove("X-XSRF-TOKEN"));
        var after = await app.Database.ScalarAsync<long>(
            "SELECT count(*) FROM tyj.audit_logs WHERE action = 'security.csrf_rejected' AND actor_id = $1", _user.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(after >= 1 && after >= before, $"監査ログの件数 {before} → {after}");
    }

    [Theory]
    [InlineData("/account/login", false)]
    [InlineData("/app", true)]
    public async Task HTMLの応答にCSPなどのヘッダーを付ける(string path, bool loggedIn)
    {
        using var client = loggedIn ? null : app.CreateTestClient();
        using var response = await (client ?? _user.Client).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; "
            + "object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; upgrade-insecure-requests",
            Header(response, "Content-Security-Policy"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
        AssertCommonHeaders(response);
        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/v1/me", true, HttpStatusCode.OK)]
    [InlineData("/api/v1/me", false, HttpStatusCode.Unauthorized)]
    [InlineData("/api/v1/no-such-api", true, HttpStatusCode.NotFound)]
    [InlineData("/health/live", false, HttpStatusCode.OK)]
    public async Task APIの応答にも共通のヘッダーを付け_キャッシュさせない(string path, bool loggedIn, HttpStatusCode status)
    {
        using var client = loggedIn ? null : app.CreateTestClient();
        using var response = await (client ?? _user.Client).GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        AssertCommonHeaders(response);
        Assert.Contains("no-store", Header(response, "Cache-Control"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/favicon.svg")]
    [InlineData("/js/passkey-submit.js")]
    public async Task 公開の静的ファイルはキャッシュを許し_使うたびに更新を確かめさせる(string path)
    {
        using var client = app.CreateTestClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertCommonHeaders(response);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task 未ログインのAPIは401のProblemDetailsを返す()
    {
        using var client = app.CreateTestClient();
        using var response = await client.GetAsync("/api/v1/teams");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.unauthenticated");
    }

    [Fact]
    public async Task 未ログインで画面を開くとログインの画面へ移る()
    {
        using var client = app.CreateTestClient();
        using var response = await client.GetAsync("/app/gantt");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", TestClient.PathOf(response.Headers.Location), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ログインのCookieは_Host_の名前でSecureとHttpOnlyとSameSiteを付ける()
    {
        var user = await TestWorld.CreateUserAsync(app, "cookies");
        using var response = await user.Client.PostLoginAsync(user.Email, user.Password);
        var cookies = response.Headers.GetValues("Set-Cookie").ToList();

        var session = Assert.Single(cookies, c => c.StartsWith(TestClient.SessionCookie + "=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("; secure", session, StringComparison.Ordinal);
        Assert.Contains("; httponly", session, StringComparison.Ordinal);
        Assert.Contains("; samesite=lax", session, StringComparison.Ordinal);
        Assert.Contains("; path=/", session, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", session, StringComparison.Ordinal);
        Assert.DoesNotContain("expires=", session, StringComparison.Ordinal);

        // 画面の JavaScript が読む CSRF のトークンは HttpOnly にしない
        using var me = await user.Client.GetAsync("/api/v1/me");
        var xsrf = Assert.Single(me.Headers.GetValues("Set-Cookie"), c => c.StartsWith(TestClient.XsrfCookie + "=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("; secure", xsrf, StringComparison.Ordinal);
        Assert.Contains("; samesite=strict", xsrf, StringComparison.Ordinal);
        Assert.DoesNotContain("httponly", xsrf, StringComparison.Ordinal);
    }

    private static void AssertCommonHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("max-age=31536000; includeSubDomains", Header(response, "Strict-Transport-Security"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Resource-Policy"));
        Assert.NotEmpty(Header(response, "X-Request-Id"));
        Assert.False(response.Headers.Contains("Server"));
        Assert.False(response.Headers.Contains("X-Powered-By"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(", ", contentValues)
        : "";

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, $"期待 {(int)status}、実際 {(int)response.StatusCode} {text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, JsonNode.Parse(text)!["code"]!.GetValue<string>());
    }
}
