using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Application.Teams;
using TaskYojitsu.Application.WorkLogs;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Tool;

/// <summary>
/// 開発環境のデモデータ。業務の処理（サービス）を通して作るため、変更履歴と監査ログも残る。
/// パスワードはコマンドの実行時に指定する（ソースコードには書かない）。
/// </summary>
internal static class DemoSeeder
{
    private static readonly (string Email, string Name, bool Admin)[] People =
    [
        ("admin@example.com", "管理 花子", true),
        ("yamada@example.com", "山田 一郎", false),
        ("suzuki@example.com", "鈴木 次郎", false),
        ("sato@example.com", "佐藤 三郎", false),
        ("tanaka@example.com", "田中 四郎", false),
    ];

    public static async Task<int> RunAsync(IServiceProvider services, string password)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.Users.AnyAsync())
        {
            throw new CommandException("利用者がすでにいるため、デモデータは作りません（空の DB で実行してください）。");
        }

        var context = services.GetRequiredService<CliRequestContext>();
        var clock = services.GetRequiredService<BusinessClock>();
        var users = services.GetRequiredService<UserManager<User>>();
        var today = clock.Today;
        DateOnly D(int days) => today.AddDays(days);

        // 祝日（2026 年と 2027 年）
        await SeedHolidaysAsync(db, clock);

        // 利用者（有効な状態で作る）
        var ids = new Dictionary<string, Guid>();
        foreach (var (email, name, admin) in People)
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = name,
                Status = UserStatus.Active,
                IsAdmin = admin,
                LockoutEnabled = true,
                CreatedAt = clock.UtcNow,
                UpdatedAt = clock.UtcNow,
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new CommandException("パスワードが規則に合いません（15 文字以上で、よく使われるものや単純な並びは使えません）: "
                    + string.Join(", ", result.Errors.Select(e => e.Code)));
            }

            ids[email] = user.Id;
        }

        Guid admin1 = ids["admin@example.com"], yamada = ids["yamada@example.com"], suzuki = ids["suzuki@example.com"],
            sato = ids["sato@example.com"], tanaka = ids["tanaka@example.com"];

        // 管理者がチームを作り、山田さんをリーダーにする
        context.UserId = admin1;
        context.AuthTime = clock.UtcNow;
        var teams = services.GetRequiredService<TeamService>();
        var sales = await teams.CreateAsync(new CreateTeamRequest("販売管理更改", "2027 年 4 月稼働の販売管理システムの更改", yamada), default);
        var portal = await teams.CreateAsync(new CreateTeamRequest("社内ポータル改善", "お知らせと検索の改善", yamada), default);

        // リーダーがメンバーを加え、タグとタスクを作る
        context.UserId = yamada;
        var members = services.GetRequiredService<MembershipService>();
        await members.AddAsync(sales.Id, new AddMemberRequest(suzuki, "member"), default);
        await members.AddAsync(sales.Id, new AddMemberRequest(sato, "member"), default);
        await members.AddAsync(portal.Id, new AddMemberRequest(tanaka, "member"), default);
        var tags = services.GetRequiredService<TagService>();
        var design = await tags.CreateAsync(sales.Id, new TagRequest("設計", "blue"), default);
        var build = await tags.CreateAsync(sales.Id, new TagRequest("実装", "green"), default);
        var important = await tags.CreateAsync(sales.Id, new TagRequest("重要", "red"), default);

        var tasks = services.GetRequiredService<TaskService>();
        async Task<GanttTaskDto> Create(Guid teamId, Guid? parent, string title, Guid? assignee, int? from, int? to, int? hours,
            Guid[]? tagIds = null, bool milestone = false, string priority = "medium") =>
            await tasks.CreateAsync(new CreateTaskRequest(teamId, parent, title, null, assignee,
                from is { } f ? D(f) : null, to is { } t ? D(t) : null, hours * 60, priority, tagIds, milestone), default);

        var requirements = await Create(sales.Id, null, "要件定義", null, null, null, null);
        var hearing = await Create(sales.Id, requirements.Id, "業務ヒアリング", suzuki, -28, -24, 16);
        var spec = await Create(sales.Id, requirements.Id, "要件定義書の作成", sato, -23, -17, 24);
        var designPhase = await Create(sales.Id, null, "設計", null, null, null, null);
        var screens = await Create(sales.Id, designPhase.Id, "画面設計", suzuki, -16, -9, 32, [design.Id]);
        var api = await Create(sales.Id, designPhase.Id, "API 設計", suzuki, -10, -2, 16, [design.Id], priority: "high");
        var dbDesign = await Create(sales.Id, designPhase.Id, "DB 設計", sato, -8, 2, 24, [design.Id]);
        await Create(sales.Id, designPhase.Id, "設計レビュー", null, 3, 3, null, milestone: true);
        var implementation = await Create(sales.Id, null, "実装", null, null, null, null);
        await Create(sales.Id, implementation.Id, "ガント実装", sato, 1, 15, 60, [build.Id], priority: "high");
        await Create(sales.Id, implementation.Id, "認証の実装", null, 5, 20, 40, [build.Id, important.Id], priority: "high");
        await Create(sales.Id, implementation.Id, "マイタスク画面", suzuki, -1, 6, 20, [build.Id]);
        await Create(sales.Id, null, "リリース", null, 30, 30, null, milestone: true);
        await Create(portal.Id, null, "お知らせ機能の改修", tanaka, -3, 7, 24);
        await Create(portal.Id, null, "検索の高速化", tanaka, 8, 20, 32, priority: "low");

        // 依存関係（要件定義書の作成 → 画面設計）
        var dependencies = services.GetRequiredService<DependencyService>();
        await dependencies.AddAsync(screens.Id, new AddDependencyRequest(spec.Id), default);

        // メンバーが自分のタスクを追加し、作業実績を入力する
        context.UserId = suzuki;
        await tasks.CreateAsync(new CreateTaskRequest(sales.Id, null, "運用手順書の作成", "バックアップと復元の手順を含める",
            suzuki, null, null, 480, "low", null, false), default);
        var hearingVersion = await LogWorkAsync(services, hearing.Id, -28, -24, 210);
        await CompleteAsync(tasks, hearing.Id, hearingVersion, D(-24));
        var screensVersion = await LogWorkAsync(services, screens.Id, -16, -8, 240);
        await CompleteAsync(tasks, screens.Id, screensVersion, D(-8));
        await LogWorkAsync(services, api.Id, -10, -1, 150, progress: 80);

        context.UserId = sato;
        var specVersion = await LogWorkAsync(services, spec.Id, -23, -17, 270);
        await CompleteAsync(tasks, spec.Id, specVersion, D(-17));
        await LogWorkAsync(services, dbDesign.Id, -8, -1, 180, progress: 40);

        context.UserId = tanaka;
        var notice = (await db.Tasks.AsNoTracking().SingleAsync(t => t.Title == "お知らせ機能の改修")).Id;
        await LogWorkAsync(services, notice, -3, -1, 240, progress: 30);

        context.UserId = null;
        Console.WriteLine("デモデータを作りました。次のアカウントで、指定したパスワードを使ってログインできます。");
        foreach (var (email, name, admin) in People)
        {
            Console.WriteLine($"  {email}  {name}{(admin ? "（管理者）" : "")}");
        }

        return 0;
    }

    /// <summary>平日ごとに作業実績を記録する。最後のタスクの版を返す。</summary>
    private static async Task<int> LogWorkAsync(IServiceProvider services, Guid taskId, int fromDay, int toDay, int minutesPerDay, int? progress = null)
    {
        var logs = services.GetRequiredService<WorkLogService>();
        var clock = services.GetRequiredService<BusinessClock>();
        var version = 0;
        var days = Enumerable.Range(fromDay, toDay - fromDay + 1)
            .Select(d => clock.Today.AddDays(d))
            .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToList();
        foreach (var date in days)
        {
            // 進捗率は、最後に記録する日に合わせて入れる
            var result = await logs.CreateAsync(taskId,
                new CreateWorkLogRequest(date, minutesPerDay, "デモの作業", date == days[^1] ? progress : null, null), default);
            version = result.Task.Version;
        }

        return version;
    }

    private static Task<GanttTaskDto> CompleteAsync(TaskService tasks, Guid taskId, int version, DateOnly end) =>
        tasks.UpdateAsync(taskId, new UpdateTaskRequest { Version = version, Status = "done", ActualEnd = end, ResultNote = "予定どおり完了" }, default);

    private static async Task SeedHolidaysAsync(AppDbContext db, BusinessClock clock)
    {
        (string Date, string Name)[] holidays =
        [
            ("2026-01-01", "元日"), ("2026-01-12", "成人の日"), ("2026-02-11", "建国記念の日"), ("2026-02-23", "天皇誕生日"),
            ("2026-03-20", "春分の日"), ("2026-04-29", "昭和の日"), ("2026-05-03", "憲法記念日"), ("2026-05-04", "みどりの日"),
            ("2026-05-05", "こどもの日"), ("2026-05-06", "休日"), ("2026-07-20", "海の日"), ("2026-08-11", "山の日"),
            ("2026-09-21", "敬老の日"), ("2026-09-22", "休日"), ("2026-09-23", "秋分の日"), ("2026-10-12", "スポーツの日"),
            ("2026-11-03", "文化の日"), ("2026-11-23", "勤労感謝の日"),
            ("2027-01-01", "元日"), ("2027-01-11", "成人の日"), ("2027-02-11", "建国記念の日"), ("2027-02-23", "天皇誕生日"),
            ("2027-03-21", "春分の日"), ("2027-03-22", "休日"), ("2027-04-29", "昭和の日"), ("2027-05-03", "憲法記念日"),
            ("2027-05-04", "みどりの日"), ("2027-05-05", "こどもの日"), ("2027-07-19", "海の日"), ("2027-08-11", "山の日"),
            ("2027-09-20", "敬老の日"), ("2027-09-23", "秋分の日"), ("2027-10-11", "スポーツの日"), ("2027-11-03", "文化の日"),
            ("2027-11-23", "勤労感謝の日"),
        ];
        foreach (var (date, name) in holidays)
        {
            var day = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
            if (!await db.Holidays.AnyAsync(h => h.HolidayDate == day))
            {
                db.Holidays.Add(new Holiday { HolidayDate = day, Name = name, CreatedAt = clock.UtcNow });
            }
        }

        await db.SaveChangesAsync();
    }
}
