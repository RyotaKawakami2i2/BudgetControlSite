using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Gantt;

public sealed record DateRange(DateOnly From, DateOnly To);

public sealed record GanttTeamDto(Guid Id, string Name, string Role, bool Archived);

public sealed record GanttMemberDto(Guid Id, string DisplayName, IReadOnlyList<Guid> TeamIds, bool Current, bool Disabled);

public sealed record HolidayDto(DateOnly Date, string Name);

/// <summary>ガントの表示用データ（API-19。詳細設計書 5.4.1）。</summary>
public sealed record GanttDto(
    DateOnly AsOf,
    DateRange Range,
    IReadOnlyList<GanttTeamDto> Teams,
    IReadOnlyList<GanttMemberDto> Members,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<HolidayDto> Holidays,
    IReadOnlyList<GanttTaskDto> Tasks,
    bool Truncated);

public sealed record GanttSearchDto(IReadOnlyList<Guid> TaskIds, bool Truncated);

/// <summary>
/// ガントの表示用データ。選んだチームのタスクを 1 回でまとめて返す。まとめタスクの集計、遅れの判定、操作の可否はサーバーで計算する。
/// 絞り込み・まとめ方・並べ替えは画面側で行う（基本設計書 8.1）。
/// </summary>
public sealed class GanttQuery(
    IAppDbContext db,
    AccessPolicy access,
    TaskDataLoader loader,
    IOptions<BusinessOptions> options)
{
    public const int MaxTeams = 20;
    public const int MaxRangeDays = 1100;

    public async Task<GanttDto> GetAsync(IReadOnlyList<Guid> teamIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var userId = access.UserId;
        var ids = teamIds.Distinct().ToList();
        var v = new Validation();
        if (ids.Count is 0 or > MaxTeams)
        {
            v.Add("teamIds", Msg.CmnChoice);
        }

        if (to < from || to.DayNumber - from.DayNumber > MaxRangeDays)
        {
            v.Add("to", Msg.CmnChoice);
        }

        v.ThrowIfAny();

        // 指定したすべてのチームで task.view を確かめる
        var teams = new List<TeamAccess>();
        foreach (var id in ids)
        {
            teams.Add(await access.RequireTeamAsync(id, Operation.TaskView, ct));
        }

        var set = await loader.LoadTeamsAsync(ids, ct);
        var today = set.Today;

        // 表示期間と重なるタスク、日程未定の未完了タスク、と、その祖先
        var selected = new HashSet<Guid>();
        foreach (var task in set.Tasks)
        {
            var r = set.Rollups[task.Id];
            var include =
                Overlaps(r.PlannedStart, r.PlannedEnd, from, to)
                || (r.ActualStart is { } aStart && Overlaps(aStart, r.ActualEnd ?? (r.Status == TaskItemStatus.Done ? aStart : today), from, to))
                || (r.PlannedStart is null && r.Status is not (TaskItemStatus.Done or TaskItemStatus.Cancelled));
            if (include && selected.Add(task.Id))
            {
                foreach (var ancestor in set.Ancestors(task.Id))
                {
                    selected.Add(ancestor.Id);
                }
            }
        }

        var tags = await loader.LoadTaskTagsAsync(ids, ct);
        var preds = await loader.LoadPredecessorsAsync(ids, ct);
        var factsByTeam = teams.ToDictionary(t => t.Team.Id, t => t.Facts);

        var max = options.Value.GanttMaxTasksPerResponse;
        var ordered = ids.SelectMany(set.HierarchyOrder).Where(t => selected.Contains(t.Id)).ToList();
        var truncated = ordered.Count > max;
        var tasks = ordered.Take(max)
            .Select(t => TaskDataLoader.ToDto(
                set,
                t,
                tags.GetValueOrDefault(t.Id) ?? [],
                [.. (preds.GetValueOrDefault(t.Id) ?? []).Where(set.ById.ContainsKey)],
                TaskDataLoader.ComputeCan(set, t, factsByTeam[t.TeamId], userId)))
            .ToList();

        // 担当者の一覧: 現在のメンバーと、表示するタスクの担当者・作成者
        var memberships = await db.TeamMembers.AsNoTracking()
            .Where(m => ids.Contains(m.TeamId) && m.RemovedAt == null)
            .Select(m => new { m.TeamId, m.UserId })
            .ToListAsync(ct);
        var referenced = tasks.SelectMany(t => new[] { t.AssigneeId, (Guid?)t.CreatedById })
            .Where(i => i.HasValue).Select(i => i!.Value);
        var userIds = memberships.Select(m => m.UserId).Concat(referenced).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Status })
            .ToListAsync(ct);
        var members = users
            .Select(u =>
            {
                var teamIdsOfUser = memberships.Where(m => m.UserId == u.Id).Select(m => m.TeamId).ToList();
                return new GanttMemberDto(u.Id, u.DisplayName, teamIdsOfUser, teamIdsOfUser.Count > 0, u.Status == UserStatus.Disabled);
            })
            .OrderBy(m => m.DisplayName, StringComparer.Ordinal)
            .ToList();

        var holidays = await db.Holidays.AsNoTracking()
            .Where(h => h.HolidayDate >= from && h.HolidayDate <= to)
            .OrderBy(h => h.HolidayDate)
            .Select(h => new HolidayDto(h.HolidayDate, h.Name))
            .ToListAsync(ct);

        return new GanttDto(
            today,
            new DateRange(from, to),
            [.. teams.Select(t => new GanttTeamDto(t.Team.Id, t.Team.Name, t.Role?.ToCode() ?? "admin_view", t.Team.IsArchived))],
            members,
            await loader.LoadTagsAsync(ids, ct),
            holidays,
            tasks,
            truncated);
    }

    /// <summary>キーワードに一致するタスクの ID（タイトル、説明、タグ。1,000 件まで。API-20）。</summary>
    public async Task<GanttSearchDto> SearchAsync(IReadOnlyList<Guid> teamIds, string? q, CancellationToken ct)
    {
        var ids = teamIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxTeams)
        {
            throw ValidationException.For("teamIds", Msg.CmnChoice);
        }

        foreach (var id in ids)
        {
            await access.RequireTeamAsync(id, Operation.TaskView, ct);
        }

        var keyword = TextRules.NormalizeSingleLine(q);
        if (keyword is null)
        {
            return new GanttSearchDto([], false);
        }

        if (TextRules.Length(keyword) > 100)
        {
            throw ValidationException.For("q", Msg.CmnTooLong);
        }

        var lower = keyword.ToLowerInvariant();
        const int limit = 1000;
        var matched = await db.Tasks.AsNoTracking()
            .Where(t => ids.Contains(t.TeamId) && t.DeletedAt == null)
            .Where(t => t.Title.ToLower().Contains(lower)
                || (t.Description != null && t.Description.ToLower().Contains(lower))
                || db.TaskTags.Any(tt => tt.TaskId == t.Id && db.Tags.Any(tag => tag.Id == tt.TagId && tag.Name.ToLower().Contains(lower))))
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .Take(limit + 1)
            .ToListAsync(ct);
        return new GanttSearchDto([.. matched.Take(limit)], matched.Count > limit);
    }

    private static bool Overlaps(DateOnly? start, DateOnly? end, DateOnly from, DateOnly to) =>
        start is { } s && end is { } e && s <= to && e >= from;
}
