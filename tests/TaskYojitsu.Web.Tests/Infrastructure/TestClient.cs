using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TaskYojitsu.Web.Tests.Infrastructure;

/// <summary>
/// ブラウザの代わりに要求を送る。Cookie を覚え、状態を変える API の要求には画面と同じく
/// CSRF のトークン（X-XSRF-TOKEN）、Origin、JSON の Content-Type を付ける。リダイレクトはたどらない。
/// </summary>
public sealed partial class TestClient : IDisposable
{
    public const string SessionCookie = "__Host-tyj.session";
    public const string XsrfCookie = "__Host-tyj.xsrf";

    private static int _nextIp;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public TestClient(AppFactory factory)
    {
        // 送信元 IP を利用者ごとに変える（ログインの回数制限が送信元ごとのため）
        var n = Interlocked.Increment(ref _nextIp);
        Ip = $"10.{(n >> 16) & 255}.{(n >> 8) & 255}.{n & 255}";
        Http = factory.CreateDefaultClient(new Uri(AppFactory.Origin), new CookieHandler(Cookies, () => Ip));
    }

    public HttpClient Http { get; }

    public CookieContainer Cookies { get; } = new();

    public string Ip { get; set; }

    public string? Cookie(string name) => Cookies.GetCookies(new Uri(AppFactory.Origin))[name]?.Value;

    /// <summary>ログインの画面からメールアドレスとパスワードでログインする。成功したら移動先の URL を返す。</summary>
    public async Task<HttpResponseMessage> PostLoginAsync(string email, string password)
    {
        var token = await GetFormTokenAsync("/account/login");
        return await Http.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));
    }

    public async Task LoginAsync(string email, string password)
    {
        using var response = await PostLoginAsync(email, password);
        if (response.StatusCode != HttpStatusCode.Redirect || PathOf(response.Headers.Location)?.StartsWith("/app", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException($"ログインできませんでした: {(int)response.StatusCode} {response.Headers.Location} {await response.Content.ReadAsStringAsync()}");
        }

        // 画面を開いたときと同じく、CSRF のトークンを受け取る
        using var me = await Http.GetAsync("/api/v1/me");
        me.EnsureSuccessStatusCode();
    }

    /// <summary>フォームの画面を開き、フォームに入っている CSRF のトークンを返す。</summary>
    public async Task<string> GetFormTokenAsync(string path)
    {
        var html = await Http.GetStringAsync(path);
        var match = FormToken().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : throw new InvalidOperationException($"{path} にフォームのトークンがありません。");
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Http.GetAsync(path);

    /// <summary>API を呼ぶ。GET 以外には、画面と同じヘッダーを付ける。</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, Action<HttpRequestMessage>? configure = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (method != HttpMethod.Get)
        {
            request.Content = body switch
            {
                null => new StringContent("", Encoding.UTF8, "application/json"),
                string raw => new StringContent(raw, Encoding.UTF8, "application/json"),
                _ => JsonContent.Create(body, options: Json),
            };
            request.Headers.Add("Origin", AppFactory.Origin);
            if (Cookie(XsrfCookie) is { } token)
            {
                request.Headers.Add("X-XSRF-TOKEN", token);
            }
        }

        configure?.Invoke(request);
        return Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body = null) => SendAsync(HttpMethod.Post, path, body);

    public Task<HttpResponseMessage> PatchAsync(string path, object? body = null) => SendAsync(HttpMethod.Patch, path, body);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    /// <summary>API を呼び、成功を確かめて JSON を返す。</summary>
    public async Task<JsonNode> JsonAsync(HttpMethod method, string path, object? body = null)
    {
        using var response = await SendAsync(method, path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{method} {path} が失敗しました: {(int)response.StatusCode} {text}");
        }

        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"{method} {path} の応答が空です。");
    }

    /// <summary>リダイレクト先のパス（絶対 URL でも相対 URL でも）。</summary>
    public static string? PathOf(Uri? location) =>
        location is null ? null : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;

    public void Dispose() => Http.Dispose();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex FormToken();

    /// <summary>Cookie を覚えて送る（ブラウザと同じく、Secure の Cookie は https のときだけ送る）。</summary>
    private sealed class CookieHandler(CookieContainer cookies, Func<string> ip) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
            {
                request.Headers.Add("Cookie", header);
            }

            request.Headers.Add(TestClientIpStartupFilter.Header, ip());
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    cookies.SetCookies(uri, value);
                }
            }

            return response;
        }
    }
}
