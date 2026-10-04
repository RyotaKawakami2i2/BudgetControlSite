using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Home;

public sealed record MyTaskDto(
    Guid Id,
    Guid TeamId,
    string TeamName,
    string Title,
    IReadOnlyList<TaskRef> Path,
    TaskItemStatus Status,
    Priority Priority,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    DateOnly? ActualStart,
    int ActualMinutes,
    int Progress,
    int? ExpectedProgress,
    IReadOnlyList<DelayFlag> Flags,
    int Version,
    TaskCan Can,
    IReadOnlyList<TaskItemStatus> NextStatuses);

/// <summary>マイタスク（API-42。詳細設計書 7.6）。</summary>
public sealed record MyTasksDto(
    DateOnly Today,
    IReadOnlyList<MyTaskDto> Overdue,
    IReadOnlyList<MyTaskDto> DueToday,
    IReadOnlyList<MyTaskDto> DueThisWeek,
    IReadOnlyList<MyTaskDto> Later,
    IReadOnlyList<MyTaskDto> Unscheduled);

/// <summary>
/// マイタスク。対象は自分が担当の、子を持たない、完了・中止以外のタスク（アーカイブしたチームのものは除く）。
/// 「遅れ」「今日まで」「今週まで」「それ以降」「日程未定」に分ける。
/// </summary>
public sealed class MyTasksQuery(
    IAppDbContext db,
    AccessPolicy access,
    TaskDataLoader loader,
    BusinessClock clock,
    IOptions<BusinessOptions> options)
{
    public async Task<MyTasksDto> GetAsync(CancellationToken ct)
    {
        var tasks = await LoadAsync(ct);
        var today = clock.Today;
        var sunday = BusinessClock.MondayOf(today).AddDays(6);
        var overdue = new List<MyTaskDto>();
        var dueToday = new List<MyTaskDto>();
        var thisWeek = new List<MyTaskDto>();
        var later = new List<MyTaskDto>();
        var unscheduled = new List<MyTaskDto>();
        foreach (var t in tasks.OrderBy(t => t.PlannedEnd ?? DateOnly.MaxValue).ThenBy(t => t.Priority).ThenBy(t => t.Title, StringComparer.Ordinal))
        {
            if (t.Flags.Contains(DelayFlag.Overdue) || t.Flags.Contains(DelayFlag.LateStart))
            {
                overdue.Add(t);
            }
            else if (t.PlannedEnd is not { } end)
            {
                unscheduled.Add(t);
            }
            else if (end == today)
            {
                dueToday.Add(t);
            }
            else if (end <= sunday)
            {
                thisWeek.Add(t);
            }
            else
            {
                later.Add(t);
            }
        }

        return new MyTasksDto(today, overdue, dueToday, thisWeek, later, unscheduled);
    }

    /// <summary>自分が担当の、子を持たない未完了のタスク（ホームでも使う）。</summary>
    public async Task<List<MyTaskDto>> LoadAsync(CancellationToken ct)
    {
        var userId = access.UserId;
        var user = await access.GetUserAsync(ct);
        var teams = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.RemovedAt == null)
            .Join(db.Teams.Where(t => t.ArchivedAt == null), m => m.TeamId, t => t.Id, (m, t) => new { t.Id, t.Name, m.Role })
            .ToListAsync(ct);
        if (teams.Count == 0)
        {
            return [];
        }

        var teamIds = teams.Select(t => t.Id).ToList();
        var mine = await db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == userId && t.DeletedAt == null && teamIds.Contains(t.TeamId)
                && t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled
                && !db.Tasks.Any(c => c.ParentId == t.Id && c.DeletedAt == null))
            .ToListAsync(ct);
        if (mine.Count == 0)
        {
            return [];
        }

        var ids = mine.Select(t => t.Id).ToList();
        var minutes = await db.WorkLogs.AsNoTracking()
            .Where(w => ids.Contains(w.TaskId))
            .GroupBy(w => w.TaskId)
            .Select(g => new { g.Key, Minutes = g.Sum(w => w.Minutes) })
            .ToDictionaryAsync(x => x.Key, x => x.Minutes, ct);

        // 親の経路（どこに属するタスクかを示す）
        var titles = await db.Tasks.AsNoTracking()
            .Where(t => teamIds.Contains(t.TeamId) && t.DeletedAt == null)
            .Select(t => new { t.Id, t.ParentId, t.Title })
            .ToDictionaryAsync(t => t.Id, ct);
        var calendar = await loader.LoadCalendarAsync(ct);
        var today = clock.Today;
        var threshold = options.Value.ProgressLagThreshold;
        var teamById = teams.ToDictionary(t => t.Id);

        return [.. mine.Select(t =>
        {
            var actual = minutes.GetValueOrDefault(t.Id);
            var expected = calendar.ExpectedProgress(t.PlannedStart, t.PlannedEnd, today);
            var flags = DelayEvaluator.Evaluate(
                new DelayInput(t.Status, t.PlannedStart, t.PlannedEnd, t.PlannedMinutes, actual, t.Progress, expected, false),
                today, threshold);
            var path = new List<TaskRef>();
            var parentId = t.ParentId;
            var guard = 0;
            while (parentId is { } pid && titles.TryGetValue(pid, out var parent) && guard++ < 8)
            {
                path.Insert(0, new TaskRef(parent.Id, parent.Title));
                parentId = parent.ParentId;
            }

            var team = teamById[t.TeamId];
            var facts = new AccessFacts
            {
                IsActiveUser = user.IsActive,
                IsAdmin = user.IsAdmin,
                Role = team.Role,
                IsCreator = t.CreatedBy == userId,
                IsAssignee = true,
                HasWorkLogs = actual > 0,
                CreatedWholeSubtree = t.CreatedBy == userId,
            };
            var can = new TaskCan(
                EditPlan: AccessPolicy.Can(Operation.TaskPlanEdit, facts),
                Assign: AccessPolicy.Can(Operation.TaskAssign, facts),
                EditActual: AccessPolicy.Can(Operation.TaskActualEdit, facts),
                LogWork: !t.IsMilestone && AccessPolicy.Can(Operation.WorklogCreate, facts),
                AddChild: !t.IsMilestone && t.Depth < HierarchyRules.MaxDepth && AccessPolicy.Can(Operation.TaskChildAdd, facts),
                Delete: AccessPolicy.Can(Operation.TaskDelete, facts));
            var next = can.EditActual
                ? StatusTransitions.NextStatuses(t.Status, new TransitionActor(team.Role == TeamRole.Leader, true, t.CreatedBy == userId))
                : [];
            return new MyTaskDto(t.Id, t.TeamId, team.Name, t.Title, path, t.Status, t.Priority, t.PlannedStart, t.PlannedEnd,
                t.PlannedMinutes, t.ActualStart, actual, t.Progress, expected, flags, t.Version, can, next);
        })];
    }
}
