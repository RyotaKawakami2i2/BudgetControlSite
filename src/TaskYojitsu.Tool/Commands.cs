using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Accounts;
using TaskYojitsu.Application.Admin;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Infrastructure.Audit;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Tool;

public sealed class CommandException(string message) : Exception(message);

/// <summary>「--名前 値」の形の引数。</summary>
public sealed class CommandOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public static CommandOptions Parse(string[] args)
    {
        var options = new CommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            options._values[name] = value;
        }

        return options;
    }

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public string Require(string name) => Get(name) ?? throw new CommandException($"--{name} を指定してください。");

    public bool Flag(string name) => Get(name) is "true";
}

public static class Commands
{
    public const string Usage = """
        タスク予実管理の運用コマンド

        使い方: taskyojitsu-tool <コマンド> [オプション]

          migrate [--list]
              DB のマイグレーションを適用する。DB の所有者の接続（db_owner_connection）で実行する（詳細設計書 12.7）
              --list を付けると、適用していないマイグレーションを表示するだけにする
          create-admin --email <メールアドレス> --name <表示名>
              最初の管理者を「招待中」で作り、招待リンクを表示する（NF-AUT-10）
          import-holidays --file <CSV のパス> [--encoding shift_jis|utf-8]
              内閣府が公表する祝日の CSV を取り込む（FR-ADM-07）
          verify-audit-log [--from <ID>]
              監査ログのハッシュ連鎖を検証する（NF-LOG-04）
          purge-audit-log [--years <年数>]
              保存期間を過ぎた監査ログを削除する。DB の所有者の接続（db_owner_connection）で実行する（NF-LOG-05）
          revoke-sessions (--email <メールアドレス> | --all)
              指定した利用者、または全員のセッションを失効させる（緊急時。NF-AUT-07）
          seed-demo --password <パスワード>
              開発環境だけ: デモ用の利用者・チーム・タスクを作る（全員に同じパスワードを設定する）
        """;

    public static async Task<int> RunAsync(string command, CommandOptions options, IServiceProvider root, IConfiguration config, bool isDevelopment)
    {
        await using var scope = root.CreateAsyncScope();
        var services = scope.ServiceProvider;
        switch (command)
        {
            case "migrate":
                return await MigrateAsync(services, options, config);
            case "create-admin":
                return await CreateAdminAsync(services, options);
            case "import-holidays":
                return await ImportHolidaysAsync(services, options);
            case "verify-audit-log":
                return await VerifyAuditLogAsync(services, options);
            case "purge-audit-log":
                return await PurgeAuditLogAsync(services, options, config);
            case "revoke-sessions":
                return await RevokeSessionsAsync(services, options);
            case "seed-demo":
                if (!isDevelopment)
                {
                    throw new CommandException("seed-demo は開発環境（ASPNETCORE_ENVIRONMENT=Development）でだけ使えます。");
                }

                return await DemoSeeder.RunAsync(services, options.Require("password"));
            default:
                Console.Error.WriteLine($"コマンド「{command}」はありません。");
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    /// <summary>
    /// マイグレーションを適用する（配備の手順 3。詳細設計書 12.7）。テーブルの作成と権限の設定には所有者の権限が要るため、
    /// アプリのアカウントではなく、配備のときだけ渡す所有者の接続で行う。
    /// </summary>
    private static async Task<int> MigrateAsync(IServiceProvider services, CommandOptions options, IConfiguration config)
    {
        var connectionString = OwnerConnection(config);
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        DbSetup.Configure(builder, connectionString);
        var identity = new ServiceCollection();
        DbSetup.ConfigureIdentityStore(identity);
        await using var provider = identity.BuildServiceProvider();
        builder.UseApplicationServiceProvider(provider);
        await using var owner = new AppDbContext(builder.Options);

        var pending = (await owner.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            Console.WriteLine("適用していないマイグレーションはありません。");
            return 0;
        }

        Console.WriteLine("適用していないマイグレーション:");
        pending.ForEach(m => Console.WriteLine($"  {m}"));
        if (options.Flag("list"))
        {
            return 0;
        }

        await owner.Database.MigrateAsync();
        await services.GetRequiredService<IAuditWriter>().WriteNowAsync(new AuditEntry("db.migrated", Detail: new { migrations = pending }));
        Console.WriteLine($"マイグレーションを {pending.Count} 件適用しました。");
        return 0;
    }

    private static string OwnerConnection(IConfiguration config) =>
        config["db_owner_connection"] ?? config.GetConnectionString("Owner")
            ?? throw new CommandException("DB の所有者の接続先（db_owner_connection または ConnectionStrings:Owner）を設定してください。");

    private static async Task<int> CreateAdminAsync(IServiceProvider services, CommandOptions options)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync(u => u.IsAdmin && u.Status != UserStatus.Disabled))
        {
            throw new CommandException("管理者がすでにいます。管理者の追加は、画面の利用者管理から招待してください。");
        }

        var admin = services.GetRequiredService<AdminUserService>();
        var result = await admin.InviteAsync(
            new InviteRequest(options.Require("email"), options.Require("name"), IsAdmin: true), CancellationToken.None, sendEmail: false);
        var link = services.GetRequiredService<InvitationService>().LinkFor(result.Token);
        Console.WriteLine($"管理者 {result.User.Email} を「招待中」で作りました。");
        Console.WriteLine("次のリンクを本人に渡し、72 時間以内にパスワードを設定してもらってください（1 回だけ使えます）。");
        Console.WriteLine("多要素認証（認証アプリかパスキー）の設定を強くおすすめします。");
        Console.WriteLine();
        Console.WriteLine(link);
        return 0;
    }

    private static async Task<int> ImportHolidaysAsync(IServiceProvider services, CommandOptions options)
    {
        var path = options.Require("file");
        if (!File.Exists(path))
        {
            throw new CommandException($"ファイル {path} がありません。");
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = (options.Get("encoding") ?? "shift_jis").ToLowerInvariant() switch
        {
            "utf-8" or "utf8" => Encoding.UTF8,
            _ => Encoding.GetEncoding("shift_jis"),
        };
        var lines = await File.ReadAllLinesAsync(path, encoding);
        var count = await services.GetRequiredService<HolidayService>().ImportAsync(lines, CancellationToken.None);
        Console.WriteLine($"祝日を {count} 件取り込みました。");
        return 0;
    }

    private static async Task<int> VerifyAuditLogAsync(IServiceProvider services, CommandOptions options)
    {
        long? from = long.TryParse(options.Get("from"), out var id) ? id : null;
        var result = await services.GetRequiredService<AuditChainVerifier>().VerifyAsync(from, CancellationToken.None);
        await services.GetRequiredService<IAuditWriter>().WriteNowAsync(new AuditEntry("audit.verified",
            result.Ok ? AuditResult.Success : AuditResult.Failure, Detail: new { result.Checked, result.FirstBrokenId, result.Reason }));
        if (result.Ok)
        {
            Console.WriteLine($"監査ログの連鎖は正常です（{result.Checked} 件を検証）。");
            return 0;
        }

        Console.Error.WriteLine($"監査ログの連鎖が途切れています。最初に食い違った記録の ID: {result.FirstBrokenId}（{result.Reason}）");
        return 1;
    }

    /// <summary>
    /// 保存期間を過ぎた監査ログを古い順に削除し、最後に削除した記録の ID とハッシュ値を検証の起点に保存する（詳細設計書 3.5）。
    /// アプリのアカウントには削除の権限がないため、DB の所有者の接続で行う。
    /// </summary>
    private static async Task<int> PurgeAuditLogAsync(IServiceProvider services, CommandOptions options, IConfiguration config)
    {
        var connectionString = OwnerConnection(config);
        var years = int.TryParse(options.Get("years"), out var y) ? y : services.GetRequiredService<IOptions<BusinessOptions>>().Value.AuditRetentionYears;
        if (years < 1)
        {
            throw new CommandException("--years は 1 以上にしてください。");
        }

        var threshold = DateTime.UtcNow.AddYears(-years);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SET LOCAL tyj.audit_purge = 'on'", connection, tx))
        {
            await set.ExecuteNonQueryAsync();
        }

        long? lastId = null;
        byte[]? lastHash = null;
        await using (var find = new NpgsqlCommand("SELECT id, hash FROM tyj.audit_logs WHERE occurred_at < @t ORDER BY id DESC LIMIT 1", connection, tx))
        {
            find.Parameters.AddWithValue("t", threshold);
            await using var reader = await find.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                lastId = reader.GetInt64(0);
                lastHash = (byte[])reader[1];
            }
        }

        var deleted = 0;
        if (lastId is { } id)
        {
            await using var delete = new NpgsqlCommand("DELETE FROM tyj.audit_logs WHERE id <= @id", connection, tx);
            delete.Parameters.AddWithValue("id", id);
            deleted = await delete.ExecuteNonQueryAsync();
            await using var anchor = new NpgsqlCommand("UPDATE tyj.audit_chain_head SET anchor_id = @id, anchor_hash = @hash WHERE id = 1", connection, tx);
            anchor.Parameters.AddWithValue("id", id);
            anchor.Parameters.AddWithValue("hash", lastHash!);
            await anchor.ExecuteNonQueryAsync();
        }

        await using (var log = new NpgsqlCommand(
            "INSERT INTO tyj.audit_logs (occurred_at, action, result, detail, request_id, user_agent) VALUES (now(), 'audit.purged', 'success', @detail::jsonb, @rid, 'taskyojitsu-tool')",
            connection, tx))
        {
            log.Parameters.AddWithValue("detail", $"{{\"deleted\":{deleted},\"years\":{years}}}");
            log.Parameters.AddWithValue("rid", services.GetRequiredService<IRequestContext>().RequestId ?? "");
            await log.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        Console.WriteLine($"{years} 年より前の監査ログを {deleted} 件削除しました。");
        return 0;
    }

    private static async Task<int> RevokeSessionsAsync(IServiceProvider services, CommandOptions options)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        int count;
        if (options.Flag("all"))
        {
            count = await db.UserSessions.Where(s => s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedReason, SessionRevokeReason.AdminRevoked));
        }
        else
        {
            var email = options.Require("email").Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == email)
                ?? throw new CommandException("その利用者は見つかりません。");
            count = await services.GetRequiredService<SessionRevoker>().RevokeAllAsync(user.Id, SessionRevokeReason.AdminRevoked, null, CancellationToken.None);
        }

        await services.GetRequiredService<IAuditWriter>().WriteNowAsync(new AuditEntry("auth.session.revoked", TargetType: "session",
            Detail: new { scope = options.Flag("all") ? "all" : "user", count, by = "tool" }));
        Console.WriteLine($"セッションを {count} 件失効させました。");
        return 0;
    }
}
