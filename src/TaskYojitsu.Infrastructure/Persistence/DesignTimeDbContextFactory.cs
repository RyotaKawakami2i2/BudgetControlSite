using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace TaskYojitsu.Infrastructure.Persistence;

/// <summary>
/// マイグレーションの作成・適用（dotnet ef）に使う。接続先は DB の所有者のアカウント（ConnectionStrings__Owner）。
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Owner")
            ?? "Host=localhost;Database=taskyojitsu;Username=tyj_owner";
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DbSetup.Configure(options, connectionString);

        // Identity のテーブルの版（パスキーを含む Version3）は、アプリのサービスにある IdentityOptions で決まる
        var services = new ServiceCollection();
        DbSetup.ConfigureIdentityStore(services);
        options.UseApplicationServiceProvider(services.BuildServiceProvider());
        return new AppDbContext(options.Options);
    }
}

/// <summary>DB の接続の設定（アプリ、運用コマンド、テストで共通）。</summary>
public static class DbSetup
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable, AppDbContext.Schema);
            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        });

    /// <summary>Identity のテーブルを、パスキーを含む版（Version3）で作るように設定する。</summary>
    public static void ConfigureIdentityStore(IServiceCollection services) =>
        services.Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
}
