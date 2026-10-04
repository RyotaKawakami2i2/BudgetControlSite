using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TaskYojitsu.Infrastructure.Audit;
using TaskYojitsu.Web.Tests.Infrastructure;
using TaskYojitsu.Web.Tests.Permissions;

namespace TaskYojitsu.Web.Tests;

/// <summary>業務の流れ（E2E-03、05、06、11 に当たる部分を API で確かめる）と監査ログ。</summary>
public sealed class BusinessFlowTests(AppFactory app, WorldFixture fixture) : IClassFixture<WorldFixture>
{
    private TestWorld World => fixture.World;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Tokyo"));

    [Fact]
    public async Task 同じ版を2人が同時に変えると後の人に409と最新の内容を返す()
    {
        var taskId = await World.CreateTaskAsync(World.LeaderA, assignee: World.MemberOwner.Id);
        var version = await World.TaskVersionAsync(taskId);

        using var first = await World.LeaderA.Client.PatchAsync($"/api/v1/tasks/{taskId}", new { version, plannedMinutes = 60 });
        using var second = await World.MemberOwner.Client.PatchAsync($"/api/v1/tasks/{taskId}", new { version, progress = 10 });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var text = await second.Content.ReadAsStringAsync(Ct);
        Assert.True(second.StatusCode == HttpStatusCode.Conflict, text);
        var problem = JsonNode.Parse(text)!;
        Assert.Equal("concurrency.conflict", problem["code"]!.GetValue<string>());
        Assert.Equal(version + 1, problem["latest"]!["version"]!.GetValue<int>());
        Assert.Equal(60, problem["latest"]!["plannedMinutes"]!.GetValue<int>());
    }

    [Fact]
    public async Task 作業実績を初めて記録すると進行中になり実績開始日が入る()
    {
        var taskId = await World.CreateTaskAsync(World.MemberOwner);
        var workDate = Today.AddDays(-2);

        var result = await World.MemberOwner.Client.JsonAsync(HttpMethod.Post, $"/api/v1/tasks/{taskId}/work-logs",
            new { workDate, minutes = 90, note = "調査", progress = 30 });

        Assert.Equal("in_progress", result["task"]!["status"]!.GetValue<string>());
        Assert.Equal(workDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), result["task"]!["actualStart"]!.GetValue<string>());
        Assert.Equal(90, result["task"]!["actualMinutes"]!.GetValue<int>());
        Assert.Equal(30, result["task"]!["progress"]!.GetValue<int>());
    }

    [Fact]
    public async Task 同じ日の作業時間の合計は24時間まで()
    {
        var first = await World.CreateTaskAsync(World.MemberOther);
        var second = await World.CreateTaskAsync(World.MemberOther);
        var workDate = Today.AddDays(-40);

        using var ok = await World.MemberOther.Client.PostAsync($"/api/v1/tasks/{first}/work-logs", new { workDate, minutes = 1200 });
        using var over = await World.MemberOther.Client.PostAsync($"/api/v1/tasks/{second}/work-logs", new { workDate, minutes = 255 });

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var problem = JsonNode.Parse(await over.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.Equal("MSG-WL-002", problem["errors"]!["minutes"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task 完了には実績終了日が必要で_進捗率は100になる()
    {
        var taskId = await World.CreateTaskAsync(World.MemberOwner);
        var version = await World.TaskVersionAsync(taskId);

        using var missing = await World.MemberOwner.Client.PatchAsync($"/api/v1/tasks/{taskId}", new { version, status = "done" });
        var done = await World.MemberOwner.Client.JsonAsync(HttpMethod.Patch, $"/api/v1/tasks/{taskId}",
            new { version, status = "done", actualEnd = Today });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("MSG-TSK-010", await missing.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("done", done["status"]!.GetValue<string>());
        Assert.Equal(100, done["progress"]!.GetValue<int>());
    }

    [Fact]
    public async Task 週の入力表でまとめて記録できる()
    {
        var member = await TestWorld.CreateUserAsync(app, "timesheet");
        await member.Client.LoginAsync(member.Email, member.Password);
        using (var join = await World.LeaderA.Client.PostAsync($"/api/v1/teams/{World.TeamA}/members", new { userId = member.Id, role = "member" }))
        {
            join.EnsureSuccessStatusCode();
        }

        var taskId = await World.CreateTaskAsync(member);
        var monday = Today.AddDays(-(((int)Today.DayOfWeek + 6) % 7) - 7);

        var saved = await member.Client.JsonAsync(HttpMethod.Put, "/api/v1/me/timesheet", new
        {
            weekStart = monday,
            cells = new[]
            {
                new { taskId, date = monday, minutes = 120 },
                new { taskId, date = monday.AddDays(1), minutes = 90 },
            },
        });

        var sheet = await member.Client.JsonAsync(HttpMethod.Get, $"/api/v1/me/timesheet?weekStart={monday:yyyy-MM-dd}");
        Assert.NotNull(saved);
        var row = sheet["rows"]!.AsArray().Single(r => r!["taskId"]!.GetValue<string>() == taskId.ToString())!;
        var minutes = row["cells"]!.AsArray().Select(c => c!["minutes"]?.GetValue<int>() ?? 0).ToArray();
        Assert.Equal([120, 90, 0, 0, 0, 0, 0], minutes);
    }

    [Fact]
    public async Task 管理者は所属していないチームを閲覧でき_監査ログに1回だけ残る()
    {
        var admin = await TestWorld.CreateUserAsync(app, "viewer-admin", isAdmin: true);
        await admin.Client.LoginAsync(admin.Email, admin.Password);
        var taskId = await World.CreateTaskAsync(World.LeaderA, assignee: World.MemberOwner.Id);

        var gantt = await admin.Client.JsonAsync(HttpMethod.Get, $"/api/v1/gantt?teamIds={World.TeamA}");
        var detail = await admin.Client.JsonAsync(HttpMethod.Get, $"/api/v1/tasks/{taskId}");
        using var change = await admin.Client.PatchAsync($"/api/v1/tasks/{taskId}", new { version = await World.TaskVersionAsync(taskId), progress = 50 });

        Assert.Contains(gantt["tasks"]!.AsArray(), t => t!["id"]!.GetValue<string>() == taskId.ToString());
        Assert.False(detail["task"]!["can"]!["editPlan"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(1, await app.Database.ScalarAsync<long>(
            "SELECT count(*) FROM tyj.audit_logs WHERE action = 'admin.team_viewed' AND actor_id = $1 AND team_id = $2", admin.Id, World.TeamA));
        Assert.True(await app.Database.ScalarAsync<long>(
            "SELECT count(*) FROM tyj.audit_logs WHERE action = 'access.denied' AND actor_id = $1", admin.Id) >= 1);
    }

    [Fact]
    public async Task 削除したタスクはリーダーが30日以内に復元できる()
    {
        var parent = await World.CreateTaskAsync(World.LeaderA);
        var child = await World.CreateTaskAsync(World.LeaderA, parentId: parent);

        using (var delete = await World.LeaderA.Client.DeleteAsync($"/api/v1/tasks/{parent}?version={await World.TaskVersionAsync(parent)}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        }

        using (var gone = await World.MemberOwner.Client.GetAsync($"/api/v1/tasks/{child}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }

        var deleted = await World.LeaderA.Client.JsonAsync(HttpMethod.Get, $"/api/v1/teams/{World.TeamA}/deleted-tasks");
        Assert.Contains(deleted.AsArray(), d => d!["id"]!.GetValue<string>() == parent.ToString() && d["descendantCount"]!.GetValue<int>() == 1);

        await World.LeaderA.Client.JsonAsync(HttpMethod.Post, $"/api/v1/tasks/{parent}/restore");
        using var back = await World.MemberOwner.Client.GetAsync($"/api/v1/tasks/{child}");
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    [Fact]
    public async Task 監査ログはハッシュの鎖がつながっていて_アプリからは書き換えられない()
    {
        await World.LeaderA.Client.JsonAsync(HttpMethod.Get, $"/api/v1/teams/{World.TeamA}");
        await World.CreateTaskAsync(World.LeaderA);

        using var scope = app.Services.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
        var result = await verifier.VerifyAsync(null, Ct);

        Assert.True(result.Ok, $"{result.FirstBrokenId} {result.Reason}");
        Assert.True(result.Checked > 0);

        // アプリのアカウントでも、所有者のアカウントでも、記録は変えられない（トリガーで拒否する）
        var error = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() =>
            app.Database.ExecuteAsync("UPDATE tyj.audit_logs SET action = 'x' WHERE id = (SELECT min(id) FROM tyj.audit_logs)"));
        Assert.NotNull(error.MessageText);
    }
}
