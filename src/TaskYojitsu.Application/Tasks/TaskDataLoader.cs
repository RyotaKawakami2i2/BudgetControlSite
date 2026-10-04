using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Tasks;

/// <summary>集計に使うタスクの列（説明などの長い項目は読まない）。</summary>
public sealed record TaskRow(
    Guid Id,
    Guid TeamId,
    Guid? ParentId,
    short Depth,
    int SortOrder,
    string Title,
    Guid? AssigneeId,
    Guid CreatedBy,
    TaskItemStatus Status,
    Priority Priority,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    short Progress,
    bool IsMilestone,
    int Version);

/// <summary>チームの論理削除されていないタスクと、その集計の結果。</summary>
public sealed class TaskSet
{
    internal TaskSet(
        IReadOnlyList<TaskRow> tasks,
        IReadOnlyDictionary<Guid, int> ownMinutes,
        IReadOnlyDictionary<Guid, RollupResult> rollups,
        WorkingCalendar calendar,
        DateOnly today)
    {
        Tasks = tasks;
        OwnMinutes = ownMinutes;
        Rollups = rollups;
        Calendar = calendar;
        Today = today;
        ById = tasks.ToDictionary(t => t.Id);
        Children = tasks
            .Where(t => t.ParentId is { } p && ById.ContainsKey(p))
            .GroupBy(t => t.ParentId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TaskRow>)[.. g.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)]);
    }

    public IReadOnlyList<TaskRow> Tasks { get; }
    public IReadOnlyDictionary<Guid, TaskRow> ById { get; }
    public IReadOnlyDictionary<Guid, IReadOnlyList<TaskRow>> Children { get; }
    public IReadOnlyDictionary<Guid, int> OwnMinutes { get; }
    public IReadOnlyDictionary<Guid, RollupResult> Rollups { get; }
    public WorkingCalendar Calendar { get; }
    public DateOnly Today { get; }

    public bool HasChildren(Guid id) => Children.ContainsKey(id);

    /// <summary>根から順に、子を並び順で並べた順序（深さ優先）。</summary>
    public IEnumerable<TaskRow> HierarchyOrder(Guid teamId)
    {
        var roots = Tasks
            .Where(t => t.TeamId == teamId && (t.ParentId is null || !ById.ContainsKey(t.ParentId.Value)))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id);
        var stack = new Stack<TaskRow>(roots.Reverse());
        while (stack.Count > 0)
        {
            var task = stack.Pop();
            yield return task;
            if (Children.TryGetValue(task.Id, out var kids))
            {
                for (var i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push(kids[i]);
                }
            }
        }
    }

    /// <summary>自分と子孫。</summary>
    public IEnumerable<TaskRow> Subtree(Guid id)
    {
        if (!ById.TryGetValue(id, out var root))
        {
            yield break;
        }

        var stack = new Stack<TaskRow>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var task = stack.Pop();
            yield return task;
            if (Children.TryGetValue(task.Id, out var kids))
            {
                foreach (var kid in kids)
                {
                    stack.Push(kid);
                }
            }
        }
    }

    /// <summary>祖先（親から根へ）。</summary>
    public IEnumerable<TaskRow> Ancestors(Guid id)
    {
        var current = ById.GetValueOrDefault(id);
        var guard = 0;
        while (current?.ParentId is { } parentId && ById.TryGetValue(parentId, out var parent) && guard++ < 16)
        {
            yield return parent;
            current = parent;
        }
    }
}

/// <summary>タスクの読み込みと集計（ガント、詳細、マイタスク、ホームで共通に使う）。</summary>
public sealed class TaskDataLoader(IAppDbContext db, BusinessClock clock, IOptions<BusinessOptions> options)
{
    public async Task<WorkingCalendar> LoadCalendarAsync(CancellationToken ct)
    {
        var holidays = await db.Holidays.AsNoTracking().Select(h => h.HolidayDate).ToListAsync(ct);
        return new WorkingCalendar(holidays);
    }

    /// <summary>指定したチームの、論理削除されていないタスクを読み込んで集計する。</summary>
    public async Task<TaskSet> LoadTeamsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var ids = teamIds.Distinct().ToList();
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => ids.Contains(t.TeamId) && t.DeletedAt == null)
            .Select(t => new TaskRow(
                t.Id, t.TeamId, t.ParentId, t.Depth, t.SortOrder, t.Title, t.AssigneeId, t.CreatedBy, t.Status, t.Priority,
                t.PlannedStart, t.PlannedEnd, t.PlannedMinutes, t.ActualStart, t.ActualEnd, t.Progress, t.IsMilestone, t.Version))
            .ToListAsync(ct);

        var minutes = await db.WorkLogs.AsNoTracking()
            .Where(w => ids.Contains(w.TeamId))
            .GroupBy(w => w.TaskId)
            .Select(g => new { TaskId = g.Key, Minutes = g.Sum(w => w.Minutes) })
            .ToDictionaryAsync(x => x.TaskId, x => x.Minutes, ct);

        var calendar = await LoadCalendarAsync(ct);
        return Build(tasks, minutes, calendar);
    }

    public TaskSet Build(IReadOnlyList<TaskRow> tasks, IReadOnlyDictionary<Guid, int> minutes, WorkingCalendar calendar)
    {
        var today = clock.Today;
        var inputs = tasks
            .Select(t => new RollupInput(
                t.Id, t.ParentId, t.Status, t.PlannedStart, t.PlannedEnd, t.PlannedMinutes, t.ActualStart, t.ActualEnd,
                t.Progress, minutes.GetValueOrDefault(t.Id)))
            .ToList();
        var rollups = RollupCalculator.Calculate(inputs, calendar, today, options.Value.ProgressLagThreshold);
        return new TaskSet(tasks, minutes, rollups, calendar, today);
    }

    /// <summary>1 件のタスクの操作の可否を求める。</summary>
    public static TaskCan ComputeCan(TaskSet set, TaskRow task, AccessFacts teamFacts, Guid userId)
    {
        if (teamFacts.Role is null)
        {
            return TaskCan.None;
        }

        var subtree = set.Subtree(task.Id).ToList();
        var facts = teamFacts with
        {
            IsCreator = task.CreatedBy == userId,
            IsAssignee = task.AssigneeId == userId,
            HasWorkLogs = subtree.Any(t => set.OwnMinutes.GetValueOrDefault(t.Id) > 0),
            CreatedWholeSubtree = subtree.All(t => t.CreatedBy == userId),
        };
        var isSummary = set.HasChildren(task.Id);
        return new TaskCan(
            EditPlan: AccessPolicy.Can(Operation.TaskPlanEdit, facts),
            Assign: AccessPolicy.Can(Operation.TaskAssign, facts),
            EditActual: AccessPolicy.Can(Operation.TaskActualEdit, facts),
            LogWork: !isSummary && !task.IsMilestone && AccessPolicy.Can(Operation.WorklogCreate, facts),
            AddChild: !task.IsMilestone && task.Depth < HierarchyRules.MaxDepth && AccessPolicy.Can(Operation.TaskChildAdd, facts),
            Delete: AccessPolicy.Can(Operation.TaskDelete, facts));
    }

    /// <summary>ガントの行の形にする。</summary>
    public static GanttTaskDto ToDto(
        TaskSet set,
        TaskRow task,
        IReadOnlyList<Guid> tagIds,
        IReadOnlyList<Guid> predecessorIds,
        TaskCan can)
    {
        var r = set.Rollups[task.Id];
        return new GanttTaskDto(
            task.Id, task.TeamId, task.ParentId, task.Depth, task.SortOrder, task.Title, task.AssigneeId, task.CreatedBy,
            r.Status, task.Priority, task.IsMilestone, r.IsSummary,
            r.PlannedStart, r.PlannedEnd, r.PlannedMinutes, r.ActualStart, r.ActualEnd, r.ActualMinutes,
            r.Progress, r.ExpectedProgress, r.Flags, r.DescendantFlagged, tagIds, predecessorIds, task.Version, can);
    }

    /// <summary>タスクに付いているタグ（タスク ID → タグ ID の一覧）。</summary>
    public async Task<Dictionary<Guid, List<Guid>>> LoadTaskTagsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var ids = teamIds.ToList();
        var rows = await db.TaskTags.AsNoTracking()
            .Where(t => ids.Contains(t.TeamId))
            .Select(t => new { t.TaskId, t.TagId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.Select(r => r.TagId).ToList());
    }

    /// <summary>先行タスク（後続タスク ID → 先行タスク ID の一覧）。</summary>
    public async Task<Dictionary<Guid, List<Guid>>> LoadPredecessorsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var ids = teamIds.ToList();
        var rows = await db.TaskDependencies.AsNoTracking()
            .Where(d => ids.Contains(d.TeamId))
            .Select(d => new { d.SuccessorId, d.PredecessorId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.SuccessorId).ToDictionary(g => g.Key, g => g.Select(r => r.PredecessorId).ToList());
    }

    /// <summary>タグの一覧。</summary>
    public async Task<List<TagDto>> LoadTagsAsync(IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var ids = teamIds.ToList();
        return await db.Tags.AsNoTracking()
            .Where(t => ids.Contains(t.TeamId))
            .OrderBy(t => t.Name)
            .Select(t => new TagDto(t.Id, t.TeamId, t.Name, t.Color))
            .ToListAsync(ct);
    }

    /// <summary>タスクの全体を読み込む（変更の前に使う）。</summary>
    public Task<TaskItem?> FindAsync(Guid id, CancellationToken ct) =>
        db.Tasks.SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct);
}
