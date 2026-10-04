using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(TaskYojitsu.Web.Tests.Infrastructure.AppFactory))]

namespace TaskYojitsu.Web.Tests.Infrastructure;

/// <summary>
/// 結合テストのアプリ。本番と同じミドルウェアの構成（開発用の設定を使わない環境）で、実際の PostgreSQL に接続して動かす（詳細設計書 13.1）。
/// 秘密情報（データ保護の証明書、ペッパー）はテストごとに作る。メールは送らずに記録する。
/// </summary>
public sealed class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Origin = "https://localhost";

    private readonly string _secretsDirectory = Path.Combine(Path.GetTempPath(), "tyj-tests-" + Guid.NewGuid().ToString("N"));

    public TestDatabase Database { get; } = new();

    public FakeEmailSender Emails { get; } = new();

    public LogCapture Logs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();

        // アプリが確かにテスト用の DB に接続していることを確かめる（開発用の DB を変えないため）
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connected = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database;
        if (connected != Database.DatabaseName)
        {
            throw new InvalidOperationException($"アプリの接続先がテスト用の DB ではありません（{connected}）。");
        }
    }

    public TestClient CreateTestClient() => new(this);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Database.DisposeAsync();
        if (Directory.Exists(_secretsDirectory))
        {
            Directory.Delete(_secretsDirectory, recursive: true);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_secretsDirectory);
        var certificatePath = Path.Combine(_secretsDirectory, "dataprotection.pfx");
        var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        WriteCertificate(certificatePath, certificatePassword);

        // UseSetting の値はコマンドラインの引数として渡るため、環境変数や appsettings.json より優先される
        builder.UseEnvironment("Testing");
        builder.UseSetting("db_app_connection", Database.AppConnection);
        builder.UseSetting("App:BaseUrl", Origin);
        builder.UseSetting("Security:Cookies:Secure", "true");
        builder.UseSetting("DataProtection:CertificatePath", certificatePath);
        builder.UseSetting("dataprotection_cert_password", certificatePassword);
        builder.UseSetting("recovery_code_pepper", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("login_hint_pepper", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("Jobs:Enabled", "false");

        // テストの準備で多くの API を呼ぶため、API 全体の回数制限だけ広げる（認証の画面の制限は本番と同じ）
        builder.UseSetting("RateLimits:ApiByUser:Permits", "100000");
        builder.UseSetting("RateLimits:ApiByIp:Permits", "100000");
        builder.UseSetting("RateLimits:AdminApiByUser:Permits", "100000");

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }

    private static void WriteCertificate(string path, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=TaskYojitsu Tests DataProtection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }
}

/// <summary>
/// テストの要求の送信元 IP を決める（TestServer には送信元がないため）。回数制限と管理のネットワークの確認に使う。
/// アプリの処理より前に動く。
/// </summary>
internal sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Client-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[Header].ToString(), out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>送ったメールを記録する。</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IEnumerable<SentEmail> To(string address) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase));

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }
}

public sealed record SentEmail(string To, string Subject, string Body);

/// <summary>アプリのログを記録する（秘密情報がログに出ていないことを確かめる。詳細設計書 13.3）。</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
        }
    }
}
