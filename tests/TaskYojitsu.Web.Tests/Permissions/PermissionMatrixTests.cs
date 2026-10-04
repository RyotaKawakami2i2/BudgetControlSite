using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TaskYojitsu.Web.Tests.Infrastructure;

namespace TaskYojitsu.Web.Tests.Permissions;

/// <summary>権限のテストの登場人物とチーム（テストのクラスで 1 回だけ作る）。</summary>
public sealed class WorldFixture(AppFactory app) : IAsyncLifetime
{
    public TestWorld World { get; private set; } = null!;

    public TestClient Anonymous { get; } = app.CreateTestClient();

    public async ValueTask InitializeAsync() => World = await TestWorld.CreateAsync(app);

    public ValueTask DisposeAsync()
    {
        Anonymous.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 権限のテスト（詳細設計書 13.1）。permission-matrix.csv の「API × 利用者の種類 × 対象」の組み合わせごとに、HTTP のステータスを確かめる。
/// 表は詳細設計書 4.5 から作る。4.5 を変えたら表も直す。対象のデータは組み合わせごとに新しく作る。
/// </summary>
public sealed partial class PermissionMatrixTests(WorldFixture fixture) : IClassFixture<WorldFixture>
{
    private static readonly string[] Actors = ["anonymous", "admin", "leader_a", "member_owner", "member_other", "leader_b", "removed"];

    private static readonly Lazy<IReadOnlyDictionary<string, Row>> Rows = new(LoadRows);

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var row in Rows.Value.Values)
        {
            foreach (var actor in Actors)
            {
                data.Add(row.Id, actor);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task 表のとおりのステータスを返す(string id, string actor)
    {
        var row = Rows.Value[id];
        var world = fixture.World;
        var values = await SetupAsync(row.Setup, world);
        var path = Fill(row.Path, values);
        var body = row.Body.Length == 0 ? null : Fill(row.Body.Replace('\'', '"'), values);
        var client = actor switch
        {
            "anonymous" => fixture.Anonymous,
            "admin" => world.Admin.Client,
            "leader_a" => world.LeaderA.Client,
            "member_owner" => world.MemberOwner.Client,
            "member_other" => world.MemberOther.Client,
            "leader_b" => world.LeaderB.Client,
            "removed" => world.Removed.Client,
            _ => throw new ArgumentOutOfRangeException(nameof(actor)),
        };

        using var response = await client.SendAsync(new HttpMethod(row.Method), path, body);

        var expected = row.Expected[actor];
        var actual = (int)response.StatusCode;
        Assert.True(expected == actual,
            $"{row.Id} {row.Operation}（{actor}）: 期待 {expected}、実際 {actual} {row.Method} {path} {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
    }

    /// <summary>対象のデータを作り、パスと本文に埋める値を返す。</summary>
    private static async Task<Dictionary<string, string>> SetupAsync(string setup, TestWorld world)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Tokyo"));
        var values = new Dictionary<string, string>
        {
            ["teamA"] = world.TeamA.ToString(),
            ["teamB"] = world.TeamB.ToString(),
            ["memberOther"] = world.MemberOther.Id.ToString(),
            ["unique"] = Guid.NewGuid().ToString("N")[..10],
            ["today"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        async Task UseTaskAsync(Guid id)
        {
            values["task"] = id.ToString();
            values["version"] = (await world.TaskVersionAsync(id)).ToString(CultureInfo.InvariantCulture);
        }

        switch (setup)
        {
            case "none":
                break;
            case "team":
                values["teamVersion"] = (await world.TeamVersionAsync(world.TeamA)).ToString(CultureInfo.InvariantCulture);
                break;
            case "new_user":
                values["user"] = (await TestWorld.CreateUserAsync(world.App, "new")).Id.ToString();
                break;
            case "team_member":
                {
                    var user = await TestWorld.CreateUserAsync(world.App, "joined");
                    await world.LeaderA.Client.JsonAsync(HttpMethod.Post, $"/api/v1/teams/{world.TeamA}/members", new { userId = user.Id, role = "member" });
                    values["user"] = user.Id.ToString();
                    break;
                }

            case "tag":
                values["tag"] = (await world.CreateTagAsync(world.LeaderA)).ToString();
                break;
            case "leader_task":
                await UseTaskAsync(await world.CreateTaskAsync(world.LeaderA, assignee: world.MemberOwner.Id));
                break;
            case "own_task":
                await UseTaskAsync(await world.CreateTaskAsync(world.MemberOwner));
                break;
            case "own_task_pair":
                await UseTaskAsync(await world.CreateTaskAsync(world.MemberOwner));
                values["task2"] = (await world.CreateTaskAsync(world.MemberOwner, title: "先行のタスク")).ToString();
                break;
            case "own_task_logged":
                {
                    var id = await world.CreateTaskAsync(world.MemberOwner);
                    await world.CreateWorkLogAsync(world.MemberOwner, id);
                    await UseTaskAsync(id);
                    break;
                }

            case "deleted_task":
                {
                    var id = await world.CreateTaskAsync(world.LeaderA, assignee: world.MemberOwner.Id);
                    using var deleted = await world.LeaderA.Client.DeleteAsync($"/api/v1/tasks/{id}?version={await world.TaskVersionAsync(id)}");
                    deleted.EnsureSuccessStatusCode();
                    values["task"] = id.ToString();
                    break;
                }

            case "own_worklog":
                {
                    var id = await world.CreateTaskAsync(world.MemberOwner);
                    var (logId, logVersion) = await world.CreateWorkLogAsync(world.MemberOwner, id);
                    values["worklog"] = logId.ToString();
                    values["worklogVersion"] = logVersion.ToString(CultureInfo.InvariantCulture);
                    break;
                }

            case "own_comment":
                {
                    var id = await world.CreateTaskAsync(world.LeaderA, assignee: world.MemberOwner.Id);
                    values["comment"] = (await world.CreateCommentAsync(world.MemberOwner, id)).ToString();
                    break;
                }

            case "archived_task":
                await UseTaskAsync(world.ArchivedTask);
                break;
            default:
                throw new InvalidOperationException($"知らない準備です: {setup}");
        }

        return values;
    }

    private static string Fill(string template, Dictionary<string, string> values)
    {
        var result = template;
        foreach (var (key, value) in values)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return Placeholder().IsMatch(result) ? throw new InvalidOperationException($"埋められていない値があります: {result}") : result;
    }

    private static IReadOnlyDictionary<string, Row> LoadRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Permissions", "permission-matrix.csv");
        var lines = File.ReadAllLines(path, Encoding.UTF8).Where(l => l.Length > 0).ToList();
        var header = ParseCsvLine(lines[0]);
        var rows = new Dictionary<string, Row>();
        foreach (var line in lines.Skip(1))
        {
            var cells = ParseCsvLine(line);
            var get = (string name) => cells[header.IndexOf(name)];
            rows.Add(get("id"), new Row(
                get("id"),
                get("operation"),
                get("method"),
                get("path"),
                get("body"),
                get("setup"),
                Actors.ToDictionary(a => a, a => int.Parse(get(a), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    /// <summary>CSV の 1 行を分ける（"" で囲んだ値の中のカンマと、"" の重ね書きに対応する）。</summary>
    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells;
    }

    [GeneratedRegex("\\{[A-Za-z0-9]+\\}")]
    private static partial Regex Placeholder();

    private sealed record Row(string Id, string Operation, string Method, string Path, string Body, string Setup, Dictionary<string, int> Expected);
}
