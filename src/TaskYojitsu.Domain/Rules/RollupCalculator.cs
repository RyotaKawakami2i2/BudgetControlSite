using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Rules;

/// <summary>集計に使うタスクの値（自分の列の値と、自分に記録された作業時間の合計）。</summary>
public sealed record RollupInput(
    Guid Id,
    Guid? ParentId,
    TaskItemStatus Status,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    int Progress,
    int OwnActualMinutes);

/// <summary>集計の結果。子を持たないタスクは自分の値、まとめタスクは子から計算した値。</summary>
public sealed record RollupResult(
    Guid Id,
    bool IsSummary,
    TaskItemStatus Status,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    int ActualMinutes,
    int Progress,
    int? ExpectedProgress,
    IReadOnlyList<DelayFlag> Flags,
    bool DescendantFlagged);

/// <summary>
/// まとめタスクの集計（要件定義書 7.3、詳細設計書 4.2）。葉から根へ順に計算する。中止したタスクは計算から除く。
/// 計算はサーバー側のここだけで行い、画面は結果を表示する。
/// </summary>
public static class RollupCalculator
{
    public static IReadOnlyDictionary<Guid, RollupResult> Calculate(
        IReadOnlyCollection<RollupInput> tasks,
        WorkingCalendar calendar,
        DateOnly today,
        int progressLagThreshold = DelayEvaluator.DefaultProgressLagThreshold)
    {
        var byId = tasks.ToDictionary(t => t.Id);
        var children = new Dictionary<Guid, List<RollupInput>>();
        foreach (var task in tasks)
        {
            // 親が集計の対象に含まれない（削除済みなど）場合は、根として扱う
            if (task.ParentId is { } parentId && byId.ContainsKey(parentId))
            {
                if (!children.TryGetValue(parentId, out var list))
                {
                    list = [];
                    children[parentId] = list;
                }

                list.Add(task);
            }
        }

        var results = new Dictionary<Guid, RollupResult>(tasks.Count);
        foreach (var task in tasks)
        {
            Compute(task);
        }

        return results;

        RollupResult Compute(RollupInput task)
        {
            if (results.TryGetValue(task.Id, out var done))
            {
                return done;
            }

            RollupResult result;
            if (!children.TryGetValue(task.Id, out var kids))
            {
                result = Leaf(task, calendar, today, progressLagThreshold);
            }
            else
            {
                var childResults = kids.Select(Compute).ToList();
                result = Summary(task, childResults, calendar, today, progressLagThreshold);
            }

            results[task.Id] = result;
            return result;
        }
    }

    private static RollupResult Leaf(RollupInput task, WorkingCalendar calendar, DateOnly today, int threshold)
    {
        var expected = calendar.ExpectedProgress(task.PlannedStart, task.PlannedEnd, today);
        var flags = DelayEvaluator.Evaluate(
            new DelayInput(task.Status, task.PlannedStart, task.PlannedEnd, task.PlannedMinutes,
                task.OwnActualMinutes, task.Progress, expected, HasChildren: false),
            today, threshold);

        return new RollupResult(task.Id, false, task.Status, task.PlannedStart, task.PlannedEnd, task.PlannedMinutes,
            task.ActualStart, task.ActualEnd, task.OwnActualMinutes, task.Progress, expected, flags, false);
    }

    private static RollupResult Summary(
        RollupInput task, List<RollupResult> children, WorkingCalendar calendar, DateOnly today, int threshold)
    {
        var active = children.Where(c => c.Status != TaskItemStatus.Cancelled).ToList();
        var descendantFlagged = children.Any(c => c.Flags.Count > 0 || c.DescendantFlagged);

        if (active.Count == 0)
        {
            // 子がすべて中止
            return new RollupResult(task.Id, true, TaskItemStatus.Cancelled, null, null, null,
                task.ActualStart, null, task.OwnActualMinutes, 0, null, [], descendantFlagged);
        }

        var plannedStart = Min(active.Select(c => c.PlannedStart));
        var plannedEnd = Max(active.Select(c => c.PlannedEnd));
        var plannedMinutes = active.All(c => c.PlannedMinutes is null)
            ? (int?)null
            : active.Sum(c => c.PlannedMinutes ?? 0);
        var actualStart = Min(active.Select(c => c.ActualStart).Append(task.ActualStart));
        var allDone = active.All(c => c.Status == TaskItemStatus.Done);
        var actualEnd = allDone ? Max(active.Select(c => c.ActualEnd)) : null;
        var actualMinutes = task.OwnActualMinutes + active.Sum(c => c.ActualMinutes);

        var status = allDone
            ? TaskItemStatus.Done
            : active.All(c => c.Status == TaskItemStatus.NotStarted)
                ? TaskItemStatus.NotStarted
                : TaskItemStatus.InProgress;

        var progress = WeightedProgress(active, calendar);
        var expected = calendar.ExpectedProgress(plannedStart, plannedEnd, today);
        var flags = DelayEvaluator.Evaluate(
            new DelayInput(status, plannedStart, plannedEnd, plannedMinutes, actualMinutes, progress, expected, HasChildren: true),
            today, threshold);

        return new RollupResult(task.Id, true, status, plannedStart, plannedEnd, plannedMinutes,
            actualStart, actualEnd, actualMinutes, progress, expected, flags, descendantFlagged);
    }

    /// <summary>
    /// 子の進捗率の重み付き平均（小数点以下は切り捨て）。重みは子の予定工数。
    /// 予定工数がない子は予定期間の稼働日数、それもなければ 1 とする。
    /// </summary>
    private static int WeightedProgress(List<RollupResult> children, WorkingCalendar calendar)
    {
        long weightSum = 0;
        long weighted = 0;
        foreach (var child in children)
        {
            long weight = child.PlannedMinutes is > 0 and var minutes
                ? minutes
                : child.PlannedStart is { } s && child.PlannedEnd is { } e
                    ? Math.Max(calendar.CountWorkingDays(s, e), 1)
                    : 1;
            weightSum += weight;
            weighted += weight * child.Progress;
        }

        return weightSum == 0 ? 0 : (int)(weighted / weightSum);
    }

    private static DateOnly? Min(IEnumerable<DateOnly?> values)
    {
        DateOnly? min = null;
        foreach (var value in values)
        {
            if (value is { } v && (min is null || v < min))
            {
                min = v;
            }
        }

        return min;
    }

    private static DateOnly? Max(IEnumerable<DateOnly?> values)
    {
        DateOnly? max = null;
        foreach (var value in values)
        {
            if (value is { } v && (max is null || v > max))
            {
                max = v;
            }
        }

        return max;
    }
}
