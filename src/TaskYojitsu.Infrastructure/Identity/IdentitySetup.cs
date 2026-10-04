using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity の設定（詳細設計書 6.1〜6.4）。アプリと運用コマンドで共通に使う。</summary>
public static class IdentitySetup
{
    public static IdentityBuilder AddAppIdentityCore(this IServiceCollection services, IConfiguration config)
    {
        DbSetup.ConfigureIdentityStore(services);
        var builder = services
            .AddIdentityCore<User>(o =>
            {
                o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                o.User.RequireUniqueEmail = true;
                o.User.AllowedUserNameCharacters = string.Empty;
                o.SignIn.RequireConfirmedEmail = true;
                o.SignIn.RequireConfirmedAccount = true;

                // パスワードの規則は PasswordPolicyValidator で検査する（文字の種類の組み合わせは強制しない）
                o.Password.RequiredLength = 1;
                o.Password.RequiredUniqueChars = 1;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;

                var lockout = config.GetSection("Security:Lockout");
                o.Lockout.MaxFailedAccessAttempts = lockout.GetValue("MaxFailedAttempts", 5);
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(lockout.GetValue("Minutes", 15));
                o.Lockout.AllowedForNewUsers = true;
                o.Tokens.AuthenticatorTokenProvider = OneTimeTotpProvider.ProviderName;
            })
            .AddUserStore<ProtectedUserStore>()
            .AddUserManager<AppUserManager>()
            .AddTokenProvider<OneTimeTotpProvider>(OneTimeTotpProvider.ProviderName);

        services.RemoveAll<IPasswordValidator<User>>();
        services.AddScoped<IPasswordValidator<User>, PasswordPolicyValidator>();
        services.AddScoped<IPasswordHasher<User>, NormalizingPasswordHasher>();
        services.Configure<PasswordHasherOptions>(o =>
        {
            o.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            o.IterationCount = config.GetValue("Security:Password:Pbkdf2Iterations", 300_000);
        });
        return builder;
    }
}
