using System.Net;
using System.Text.Json.Nodes;
using TaskYojitsu.Web.Tests.Infrastructure;

namespace TaskYojitsu.Web.Tests;

/// <summary>ログイン、ロック、セッション、再認証（E2E-07〜09、詳細設計書 6章）。</summary>
public sealed class AuthTests(AppFactory app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task 登録のないメールアドレスでも_パスワードの誤りと同じ応答を返す()
    {
        var user = await TestWorld.CreateUserAsync(app, "enum");
        using var unknown = app.CreateTestClient();

        using var wrongPassword = await user.Client.PostLoginAsync(user.Email, "wrong-password-0123456789");
        using var noAccount = await unknown.PostLoginAsync("nobody-" + Guid.NewGuid().ToString("N") + "@test.example", "wrong-password-0123456789");

        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, noAccount.StatusCode);
        const string message = "メールアドレス、パスワード、認証コードのいずれかが正しくありません。";
        Assert.Contains(message, WebUtility.HtmlDecode(await wrongPassword.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
        Assert.Contains(message, WebUtility.HtmlDecode(await noAccount.Content.ReadAsStringAsync(Ct)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 続けて5回失敗するとロックされメールが届く()
    {
        var user = await TestWorld.CreateUserAsync(app, "lockout");

        HttpResponseMessage? last = null;
        for (var i = 0; i < 5; i++)
        {
            last?.Dispose();
            last = await user.Client.PostLoginAsync(user.Email, $"wrong-password-{i}-0123456789");
        }

        Assert.Equal(HttpStatusCode.Redirect, last!.StatusCode);
        Assert.Equal("/account/lockout", TestClient.PathOf(last.Headers.Location));
        last.Dispose();

        // ロック中は、正しいパスワードでもログインできない
        using var correct = await user.Client.PostLoginAsync(user.Email, user.Password);
        Assert.Equal("/account/lockout", TestClient.PathOf(correct.Headers.Location));
        Assert.Null(user.Client.Cookie(TestClient.SessionCookie));

        var mail = Assert.Single(app.Emails.To(user.Email));
        Assert.Contains("ロック", mail.Subject, StringComparison.Ordinal);
        Assert.Equal(1, await app.Database.ScalarAsync<long>(
            "SELECT count(*) FROM tyj.audit_logs WHERE action = 'auth.login.locked' AND target_id = $1", user.Id.ToString()));
    }

    [Fact]
    public async Task 操作しないまま30分たつとセッションが切れる()
    {
        var user = await LoginAsync("idle");

        await app.Database.ExecuteAsync(
            "UPDATE tyj.user_sessions SET last_seen_at = now() - interval '31 minutes' WHERE user_id = $1", user.Id);
        using var response = await user.Client.GetAsync("/api/v1/me");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task 操作していても12時間でセッションが切れる()
    {
        var user = await LoginAsync("absolute");

        await app.Database.ExecuteAsync(
            "UPDATE tyj.user_sessions SET expires_at = now() - interval '1 second' WHERE user_id = $1", user.Id);
        using var response = await user.Client.GetAsync("/api/v1/me");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task ログアウトするとサーバー側のセッションも無効になる()
    {
        var user = await LoginAsync("logout");
        var cookie = user.Client.Cookie(TestClient.SessionCookie)!;

        using var logout = await user.Client.SendAsync(HttpMethod.Post, "/account/logout", configure: r => r.Headers.Add("Accept", "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // 古い Cookie を送り直しても使えない
        using var replay = app.CreateTestClient();
        replay.Cookies.Add(new Uri(AppFactory.Origin), new Cookie(TestClient.SessionCookie, cookie) { Secure = true, Path = "/" });
        using var response = await replay.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 管理の操作は最後の認証から10分を過ぎると再認証を求める()
    {
        var admin = await TestWorld.CreateUserAsync(app, "reauth-admin", isAdmin: true);
        await admin.Client.LoginAsync(admin.Email, admin.Password);
        using (var fresh = await admin.Client.GetAsync("/api/v1/admin/users"))
        {
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        }

        await app.Database.ExecuteAsync(
            "UPDATE tyj.user_sessions SET auth_time = now() - interval '11 minutes' WHERE user_id = $1", admin.Id);
        using (var stale = await admin.Client.GetAsync("/api/v1/admin/users"))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Forbidden, "auth.reauth_required");
        }

        // パスワードで再認証すると、また管理の操作ができる。セッションの ID は新しくなり、古い ID は使えなくなる
        var oldSession = admin.Client.Cookie(TestClient.SessionCookie)!;
        var token = await admin.Client.GetFormTokenAsync("/account/reauthenticate?returnUrl=%2Fapp%2Fadmin%2Fusers");
        using var reauth = await admin.Client.Http.PostAsync("/account/reauthenticate?returnUrl=%2Fapp%2Fadmin%2Fusers", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "reauthenticate",
            ["__RequestVerificationToken"] = token,
            ["Input.Password"] = admin.Password,
        }), Ct);
        Assert.Equal(HttpStatusCode.Redirect, reauth.StatusCode);
        Assert.Equal("/app/admin/users", TestClient.PathOf(reauth.Headers.Location));

        using var again = await admin.Client.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.NotEqual(oldSession, admin.Client.Cookie(TestClient.SessionCookie));

        using var replay = app.CreateTestClient();
        replay.Cookies.Add(new Uri(AppFactory.Origin), new Cookie(TestClient.SessionCookie, oldSession) { Secure = true, Path = "/" });
        using var old = await replay.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task 無効にした利用者のセッションはすぐに使えなくなる()
    {
        var admin = await TestWorld.CreateUserAsync(app, "disable-admin", isAdmin: true);
        await admin.Client.LoginAsync(admin.Email, admin.Password);
        var user = await LoginAsync("disabled");

        using var disable = await admin.Client.PostAsync($"/api/v1/admin/users/{user.Id}/disable");
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        using var response = await user.Client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // 無効にした利用者は、正しいパスワードでもログインできない
        using var fresh = app.CreateTestClient();
        using var login = await fresh.PostLoginAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Null(fresh.Cookie(TestClient.SessionCookie));
    }

    [Fact]
    public async Task ログにパスワードとセッションのCookieの値を出さない()
    {
        var user = await TestWorld.CreateUserAsync(app, "logs");
        using (await user.Client.PostLoginAsync(user.Email, user.Password + "-wrong"))
        {
        }

        await user.Client.LoginAsync(user.Email, user.Password);
        var session = user.Client.Cookie(TestClient.SessionCookie)!;
        var xsrf = user.Client.Cookie(TestClient.XsrfCookie)!;
        using (await user.Client.PostAsync("/api/v1/notifications/read-all"))
        {
        }

        var leaked = app.Logs.Entries.Where(e =>
            e.Contains(user.Password, StringComparison.Ordinal)
            || e.Contains(session, StringComparison.Ordinal)
            || e.Contains(xsrf, StringComparison.Ordinal)).ToList();
        Assert.Empty(leaked);
    }

    private async Task<TestUser> LoginAsync(string label)
    {
        var user = await TestWorld.CreateUserAsync(app, label);
        await user.Client.LoginAsync(user.Email, user.Password);
        return user;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(status == response.StatusCode, $"期待 {(int)status}、実際 {(int)response.StatusCode} {text}");
        Assert.Equal(code, JsonNode.Parse(text)!["code"]!.GetValue<string>());
    }
}
