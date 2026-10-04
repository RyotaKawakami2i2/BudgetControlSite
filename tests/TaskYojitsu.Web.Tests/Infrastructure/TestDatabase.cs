using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaskYojitsu.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace TaskYojitsu.Web.Tests.Infrastructure;

/// <summary>
/// 結合テストの DB（詳細設計書 13.1）。環境変数 ConnectionStrings__Test があればその DB（開発コンテナ）を、
/// なければ Testcontainers で PostgreSQL 18 を起動して使う（CI）。テストの開始時にスキーマを作り直し、マイグレーションを適用する。
/// アプリは、本番と同じく権限を絞ったアカウント（tyj_app）で接続する。ConnectionStrings__TestApp がなければ所有者で接続する。
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private const string Image = "postgres:18.6";
    private PostgreSqlContainer? _container;

    public string OwnerConnection { get; private set; } = "";

    public string AppConnection { get; private set; } = "";

    public string DatabaseName => new NpgsqlConnectionStringBuilder(OwnerConnection).Database ?? "";

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Test");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            OwnerConnection = configured;
            var app = Environment.GetEnvironmentVariable("ConnectionStrings__TestApp");
            AppConnection = string.IsNullOrWhiteSpace(app) ? configured : app;
        }
        else
        {
            await StartContainerAsync();
        }

        if (!DatabaseName.EndsWith("_test", StringComparison.Ordinal))
        {
            // 開発用の DB を誤って消さないため、テスト用の名前の DB だけを使う
            throw new InvalidOperationException($"結合テストの DB の名前は _test で終わる必要があります（{DatabaseName}）。");
        }

        await ResetSchemaAsync();
        await MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>所有者のアカウントで SQL を実行する（テストの準備で、時刻をずらすときなどに使う）。</summary>
    public async Task<int> ExecuteAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>所有者のアカウントで値を 1 つ読む。</summary>
    public async Task<T?> ScalarAsync<T>(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(OwnerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter });
        }

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    private async Task StartContainerAsync()
    {
        _container = new PostgreSqlBuilder(Image).Build();
        await _container.StartAsync();

        // 本番と同じく、所有者（マイグレーション用）とアプリ用のアカウントを分ける（.devcontainer/db/init と同じ設定）
        var ownerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var appPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            foreach (var sql in new[]
            {
                $"CREATE ROLE tyj_owner LOGIN PASSWORD '{ownerPassword}'",
                $"CREATE ROLE tyj_app LOGIN PASSWORD '{appPassword}'",
                "CREATE DATABASE taskyojitsu_test OWNER tyj_owner",
            })
            {
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
        }

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "taskyojitsu_test",
            Username = "tyj_owner",
            Password = ownerPassword,
        };
        await using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "REVOKE ALL ON DATABASE taskyojitsu_test FROM PUBLIC; GRANT CONNECT ON DATABASE taskyojitsu_test TO tyj_app; REVOKE ALL ON SCHEMA public FROM PUBLIC;",
                connection);
            await command.ExecuteNonQueryAsync();
        }

        OwnerConnection = builder.ConnectionString;
        builder.Username = "tyj_app";
        builder.Password = appPassword;
        AppConnection = builder.ConnectionString;
    }

    private Task ResetSchemaAsync() => ExecuteAsync("""
        DROP SCHEMA IF EXISTS tyj CASCADE;
        CREATE SCHEMA tyj AUTHORIZATION tyj_owner;
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tyj_app') THEN
                GRANT USAGE ON SCHEMA tyj TO tyj_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tyj_app;
                ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj GRANT USAGE, SELECT ON SEQUENCES TO tyj_app;
            END IF;
        END
        $$;
        """);

    private async Task MigrateAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        DbSetup.Configure(options, OwnerConnection);
        var services = new ServiceCollection();
        DbSetup.ConfigureIdentityStore(services);
        await using var provider = services.BuildServiceProvider();
        options.UseApplicationServiceProvider(provider);
        await using var db = new AppDbContext(options.Options);
        await db.Database.MigrateAsync();
    }
}
