using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Gantt;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Home;

/// <summary>担当者別の予定・実績・差（userId が null の行は未割り当て）。</summary>
public sealed record TeamReportRowDto(
    Guid? UserId,
    string DisplayName,
    bool CurrentMember,
    int TaskCount,
    int PlannedMinutes,
    int ActualMinutes,
    int VarianceMinutes,
    int DelayedCount);

/// <summary>チームの予実（API-44。FR-DSH-02）。</summary>
public sealed record TeamReportDto(
    Guid TeamId,
    string TeamName,
    DateRange Range,
    IReadOnlyList<TeamReportRowDto> Rows,
    TeamReportRowDto Total);

public sealed class TeamReportQuery(IAppDbContext db, AccessPolicy access, TaskDataLoader loader)
{
    public async Task<TeamReportDto> GetAsync(Guid teamId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > GanttQuery.MaxRangeDays)
        {
            throw ValidationException.For("to", Msg.CmnChoice);
        }

        var acc = await access.RequireTeamAsync(teamId, Operation.ReportTeam, ct);
        var set = await loader.LoadTeamsAsync([teamId], ct);

        // 予定工数は、中止以外の、子を持たないタスクを期間で按分する（ほかのチームの分は含めない）
        var leaves = set.Tasks
            .Where(t => !set.HasChildren(t.Id))
            .Select(t => (Task: t, Rollup: set.Rollups[t.Id]))
            .Where(x => x.Rollup.Status != TaskItemStatus.Cancelled)
            .ToList();
        var planned = leaves
            .GroupBy(x => x.Task.AssigneeId)
            .ToDictionary(
                g => g.Key ?? Guid.Empty,
                g => (
                    Minutes: g.Sum(x => EffortRules.ProratedMinutes(set.Calendar, x.Rollup.PlannedStart, x.Rollup.PlannedEnd, x.Rollup.PlannedMinutes, from, to)),
                    Count: g.Count(x => Overlaps(x.Rollup.PlannedStart, x.Rollup.PlannedEnd, from, to)),
                    Delayed: g.Count(x => x.Rollup.Flags.Contains(DelayFlag.Overdue) || x.Rollup.Flags.Contains(DelayFlag.LateStart))));

        var cancelled = set.Tasks.Where(t => set.Rollups[t.Id].Status == TaskItemStatus.Cancelled).Select(t => t.Id).ToList();
        var actual = await db.WorkLogs.AsNoTracking()
            .Where(w => w.TeamId == teamId && w.WorkDate >= from && w.WorkDate <= to && !cancelled.Contains(w.TaskId))
            .Join(db.Tasks.Where(t => t.DeletedAt == null), w => w.TaskId, t => t.Id, (w, t) => w)
            .GroupBy(w => w.UserId)
            .Select(g => new { g.Key, Minutes = g.Sum(w => w.Minutes) })
            .ToDictionaryAsync(x => x.Key, x => x.Minutes, ct);

        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.RemovedAt == null)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        var userIds = members.Concat(planned.Keys.Where(k => k != Guid.Empty)).Concat(actual.Keys).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var rows = userIds
            .Select(id =>
            {
                var p = planned.GetValueOrDefault(id);
                var plannedMinutes = (int)Math.Round(p.Minutes);
                var actualMinutes = actual.GetValueOrDefault(id);
                return new TeamReportRowDto(id, names.GetValueOrDefault(id, ""), members.Contains(id), p.Count, plannedMinutes,
                    actualMinutes, actualMinutes - plannedMinutes, p.Delayed);
            })
            .OrderBy(r => r.DisplayName, StringComparer.Ordinal)
            .ToList();
        if (planned.TryGetValue(Guid.Empty, out var unassigned))
        {
            var minutes = (int)Math.Round(unassigned.Minutes);
            rows.Add(new TeamReportRowDto(null, "未割り当て", false, unassigned.Count, minutes, 0, -minutes, unassigned.Delayed));
        }

        var total = new TeamReportRowDto(null, "合計", false, rows.Sum(r => r.TaskCount), rows.Sum(r => r.PlannedMinutes),
            rows.Sum(r => r.ActualMinutes), rows.Sum(r => r.VarianceMinutes), rows.Sum(r => r.DelayedCount));
        return new TeamReportDto(teamId, acc.Team.Name, new DateRange(from, to), rows, total);
    }

    private static bool Overlaps(DateOnly? start, DateOnly? end, DateOnly from, DateOnly to) =>
        start is { } s && end is { } e && s <= to && e >= from;
}
