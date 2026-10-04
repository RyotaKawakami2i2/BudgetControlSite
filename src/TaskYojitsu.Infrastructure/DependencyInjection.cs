using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Infrastructure.Audit;
using TaskYojitsu.Infrastructure.Email;
using TaskYojitsu.Infrastructure.Identity;
using TaskYojitsu.Infrastructure.Persistence;
using TaskYojitsu.Infrastructure.Sessions;

namespace TaskYojitsu.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// DB、監査ログ、メール、データ保護などを登録する。秘密情報は、Docker の secrets（/run/secrets のファイル）か、
    /// 開発環境では環境変数から読む（詳細設計書 11章。設定ファイルには書かない）。
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool isDevelopment)
    {
        var connectionString = config["db_app_connection"] ?? config.GetConnectionString("App")
            ?? throw new InvalidOperationException("DB の接続先（db_app_connection または ConnectionStrings:App）が設定されていません。");

        services.AddDbContextFactory<AppDbContext>(options => DbSetup.Configure(options, connectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDbLocks, DbLocks>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditChainVerifier>();

        services.Configure<SmtpOptions>(config.GetSection(SmtpOptions.Section));
        services.PostConfigure<SmtpOptions>(o => o.Password ??= config["smtp_password"]);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddSingleton<CommonPasswordList>();
        services.AddSingleton(new RecoveryCodeHasher(PepperLoader.Load(config["recovery_code_pepper"], isDevelopment, "recovery_code_pepper")));
        services.AddSingleton(new LoginHintHasher(PepperLoader.Load(config["login_hint_pepper"], isDevelopment, "login_hint_pepper")));
        services.AddSingleton<DbTicketStore>();

        // 漏えい照合サービス（既定は無効。社内サーバーから外へ出る場合はプロキシを使う）
        services.AddHttpClient("pwned", client =>
            {
                client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var proxy = config["Security:Password:PwnedApi:ProxyUrl"];
                return new HttpClientHandler
                {
                    Proxy = string.IsNullOrEmpty(proxy) ? null : new WebProxy(proxy),
                    UseProxy = !string.IsNullOrEmpty(proxy),
                };
            });

        // データ保護の鍵は DB に保存し、証明書で暗号化する（90 日ごとに自動で更新される。詳細設計書 7.6）
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("TaskYojitsu")
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90))
            .PersistKeysToDbContext<AppDbContext>();
        var certificatePath = config["DataProtection:CertificatePath"] ?? "/run/secrets/dataprotection_cert";
        if (File.Exists(certificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, config["dataprotection_cert_password"]);
            dataProtection.ProtectKeysWithCertificate(certificate);
        }
        else if (!isDevelopment)
        {
            throw new InvalidOperationException("データ保護の鍵を暗号化する証明書（dataprotection_cert）がありません。");
        }

        return services;
    }
}
