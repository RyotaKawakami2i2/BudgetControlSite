using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Web.Tests.Infrastructure;

/// <summary>テストの利用者（ログインした状態のクライアントを持つ）。</summary>
public sealed record TestUser(Guid Id, string Email, string Password, TestClient Client);

/// <summary>
/// 権限のテストの登場人物とチーム（詳細設計書 13.1）。メールアドレスとチーム名には毎回違う値を付け、
/// ほかのテストと同じ DB を使っても混ざらないようにする。
/// </summary>
public sealed class TestWorld
{
    private TestWorld(AppFactory app) => App = app;

    public AppFactory App { get; }

    public Guid TeamA { get; private set; }

    public Guid TeamB { get; private set; }

    /// <summary>アーカイブしたチーム（リーダーは LeaderA だけ）。</summary>
    public Guid ArchivedTeam { get; private set; }

    /// <summary>アーカイブしたチームのタスク。</summary>
    public Guid ArchivedTask { get; private set; }

    public required TestUser Admin { get; init; }

    public required TestUser LeaderA { get; init; }

    /// <summary>A チームのメンバー（自分のタスクの作成者かつ担当者になる）。</summary>
    public required TestUser MemberOwner { get; init; }

    /// <summary>A チームのメンバー（対象のタスクの作成者でも担当者でもない）。</summary>
    public required TestUser MemberOther { get; init; }

    /// <summary>B チームのリーダー（A チームの外）。</summary>
    public required TestUser LeaderB { get; init; }

    /// <summary>A チームから外された元メンバー。</summary>
    public required TestUser Removed { get; init; }

    public static async Task<TestWorld> CreateAsync(AppFactory app)
    {
        var world = new TestWorld(app)
        {
            Admin = await CreateUserAsync(app, "admin", isAdmin: true),
            LeaderA = await CreateUserAsync(app, "leader-a"),
            MemberOwner = await CreateUserAsync(app, "member-owner"),
            MemberOther = await CreateUserAsync(app, "member-other"),
            LeaderB = await CreateUserAsync(app, "leader-b"),
            Removed = await CreateUserAsync(app, "removed"),
        };

        world.TeamA = await CreateTeamAsync(app, "A", [
            (world.LeaderA.Id, TeamRole.Leader, false),
            (world.MemberOwner.Id, TeamRole.Member, false),
            (world.MemberOther.Id, TeamRole.Member, false),
            (world.Removed.Id, TeamRole.Member, true),
        ]);
        world.TeamB = await CreateTeamAsync(app, "B", [(world.LeaderB.Id, TeamRole.Leader, false)]);
        world.ArchivedTeam = await CreateTeamAsync(app, "Archived", [(world.LeaderA.Id, TeamRole.Leader, false)]);

        foreach (var user in world.All)
        {
            await user.Client.LoginAsync(user.Email, user.Password);
        }

        // アーカイブする前にタスクを作っておく
        world.ArchivedTask = await world.CreateTaskAsync(world.LeaderA, world.ArchivedTeam, assignee: world.LeaderA.Id);
        await app.Database.ExecuteAsync("UPDATE tyj.teams SET archived_at = now(), archived_by = $1 WHERE id = $2", world.LeaderA.Id, world.ArchivedTeam);
        return world;
    }

    public IEnumerable<TestUser> All => [Admin, LeaderA, MemberOwner, MemberOther, LeaderB, Removed];

    /// <summary>有効な利用者を作る（ログインはしない）。</summary>
    public static async Task<TestUser> CreateUserAsync(AppFactory app, string label, bool isAdmin = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"{label}-{suffix}@test.example";
        var password = $"tyj-pass-{Guid.NewGuid():N}";
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = $"{label} {suffix[..4]}",
            Status = UserStatus.Active,
            IsAdmin = isAdmin,
            LockoutEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("利用者を作れませんでした: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        return new TestUser(user.Id, email, password, app.CreateTestClient());
    }

    private static async Task<Guid> CreateTeamAsync(AppFactory app, string label, (Guid User, TeamRole Role, bool Removed)[] members)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var team = new Team
        {
            Id = Guid.CreateVersion7(),
            Name = $"{label} {Guid.NewGuid().ToString("N")[..12]}",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Teams.Add(team);
        foreach (var (user, role, removed) in members)
        {
            db.TeamMembers.Add(new TeamMember
            {
                TeamId = team.Id,
                UserId = user,
                Role = role,
                JoinedAt = now.AddDays(-30),
                RemovedAt = removed ? now.AddDays(-1) : null,
            });
        }

        await db.SaveChangesAsync();
        return team.Id;
    }

    // ---------------------------------------------------------------- API を通したデータの準備

    public async Task<Guid> CreateTaskAsync(TestUser by, Guid? teamId = null, Guid? assignee = null, Guid? parentId = null, string title = "テストのタスク")
    {
        var body = new JsonObject
        {
            ["teamId"] = (teamId ?? TeamA).ToString(),
            ["title"] = title,
        };
        if (assignee is { } a)
        {
            body["assigneeId"] = a.ToString();
        }

        if (parentId is { } p)
        {
            body["parentId"] = p.ToString();
        }

        var task = await by.Client.JsonAsync(HttpMethod.Post, "/api/v1/tasks", body);
        return Guid.Parse(task["id"]!.GetValue<string>());
    }

    public async Task<int> TaskVersionAsync(Guid taskId) =>
        await App.Database.ScalarAsync<int>("SELECT version FROM tyj.tasks WHERE id = $1", taskId);

    public async Task<int> TeamVersionAsync(Guid teamId) =>
        await App.Database.ScalarAsync<int>("SELECT version FROM tyj.teams WHERE id = $1", teamId);

    public async Task<(Guid Id, int Version)> CreateWorkLogAsync(TestUser by, Guid taskId, int minutes = 30)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Tokyo"));
        var result = await by.Client.JsonAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/work-logs", new { workDate = today, minutes });
        var log = result["workLog"]!;
        return (Guid.Parse(log["id"]!.GetValue<string>()), log["version"]!.GetValue<int>());
    }

    public async Task<Guid> CreateCommentAsync(TestUser by, Guid taskId)
    {
        var comment = await by.Client.JsonAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/comments", new { body = "準備のコメント" });
        return Guid.Parse(comment["id"]!.GetValue<string>());
    }

    public async Task<Guid> CreateTagAsync(TestUser by)
    {
        var tag = await by.Client.JsonAsync(HttpMethod.Post, $"/api/v1/teams/{TeamA}/tags", new { name = "タグ" + Guid.NewGuid().ToString("N")[..8], color = "blue" });
        return Guid.Parse(tag["id"]!.GetValue<string>());
    }
}
