using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Home;

public sealed record DashboardBucket(int Count, IReadOnlyList<MyTaskDto> Items);

/// <summary>所属チーム（プロジェクト）ごとの状況。</summary>
public sealed record TeamStatusDto(
    Guid TeamId,
    string Name,
    string Role,
    int TaskCount,
    int DoneCount,
    int CompletionRate,
    int DelayedCount,
    int PlannedMinutesToDate,
    int ActualMinutesToDate,
    DateOnly? Start,
    DateOnly? End,
    int Progress);

/// <summary>ホームの要約（API-43。FR-DSH-01）。</summary>
public sealed record DashboardDto(
    DateOnly Today,
    DashboardBucket Overdue,
    DashboardBucket DueToday,
    DashboardBucket DueThisWeek,
    IReadOnlyList<TeamStatusDto> Teams);

public sealed class DashboardQuery(
    IAppDbContext db,
    AccessPolicy access,
    MyTasksQuery myTasks,
    TaskDataLoader loader,
    BusinessClock clock)
{
    private const int ListLimit = 10;

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var userId = access.UserId;
        var mine = await myTasks.GetAsync(ct);
        var today = clock.Today;

        var teams = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.RemovedAt == null)
            .Join(db.Teams.Where(t => t.ArchivedAt == null), m => m.TeamId, t => t.Id, (m, t) => new { t.Id, t.Name, m.Role })
            .ToListAsync(ct);
        var teamIds = teams.Select(t => t.Id).ToList();
        var set = await loader.LoadTeamsAsync(teamIds, ct);
        var actualToDate = await db.WorkLogs.AsNoTracking()
            .Where(w => teamIds.Contains(w.TeamId) && w.WorkDate <= today)
            .Join(db.Tasks.Where(t => t.DeletedAt == null && t.Status != TaskItemStatus.Cancelled), w => w.TaskId, t => t.Id, (w, t) => w)
            .GroupBy(w => w.TeamId)
            .Select(g => new { g.Key, Minutes = g.Sum(w => w.Minutes) })
            .ToDictionaryAsync(x => x.Key, x => x.Minutes, ct);

        var statuses = teams
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(team =>
            {
                // 完了率は、中止を除く、子を持たないタスクの件数で計算する
                var leaves = set.Tasks
                    .Where(t => t.TeamId == team.Id && !set.HasChildren(t.Id))
                    .Select(t => set.Rollups[t.Id])
                    .Where(r => r.Status != TaskItemStatus.Cancelled)
                    .ToList();
                var done = leaves.Count(r => r.Status == TaskItemStatus.Done);
                var delayed = leaves.Count(r => r.Flags.Contains(DelayFlag.Overdue) || r.Flags.Contains(DelayFlag.LateStart));
                var start = leaves.Where(r => r.PlannedStart is not null).Select(r => r.PlannedStart).Min();
                var end = leaves.Where(r => r.PlannedEnd is not null).Select(r => r.PlannedEnd).Max();
                var planned = start is { } s
                    ? leaves.Sum(r => EffortRules.ProratedMinutes(set.Calendar, r.PlannedStart, r.PlannedEnd, r.PlannedMinutes, s, today))
                    : 0;
                var roots = set.HierarchyOrder(team.Id).Where(t => t.ParentId is null || !set.ById.ContainsKey(t.ParentId.Value))
                    .Select(t => set.Rollups[t.Id])
                    .Where(r => r.Status != TaskItemStatus.Cancelled)
                    .ToList();
                var progress = WeightedProgress(roots, set.Calendar);
                return new TeamStatusDto(
                    team.Id, team.Name, team.Role.ToCode(), leaves.Count, done,
                    leaves.Count == 0 ? 0 : done * 100 / leaves.Count,
                    delayed, (int)Math.Round(planned), actualToDate.GetValueOrDefault(team.Id), start, end, progress);
            })
            .ToList();

        return new DashboardDto(
            today,
            new DashboardBucket(mine.Overdue.Count, [.. mine.Overdue.Take(ListLimit)]),
            new DashboardBucket(mine.DueToday.Count, [.. mine.DueToday.Take(ListLimit)]),
            new DashboardBucket(mine.DueThisWeek.Count, [.. mine.DueThisWeek.Take(ListLimit)]),
            statuses);
    }

    private static int WeightedProgress(List<RollupResult> tasks, WorkingCalendar calendar)
    {
        long sum = 0;
        long weighted = 0;
        foreach (var r in tasks)
        {
            long weight = r.PlannedMinutes is > 0 and var m
                ? m
                : r.PlannedStart is { } s && r.PlannedEnd is { } e ? Math.Max(calendar.CountWorkingDays(s, e), 1) : 1;
            sum += weight;
            weighted += weight * r.Progress;
        }

        return sum == 0 ? 0 : (int)(weighted / sum);
    }
}
