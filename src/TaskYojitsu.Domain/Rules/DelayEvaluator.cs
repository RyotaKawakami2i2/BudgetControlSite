using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Rules;

/// <summary>判定に使うタスクの値。まとめタスクは、子から計算した値を渡す。</summary>
public readonly record struct DelayInput(
    TaskItemStatus Status,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    int ActualMinutes,
    int Progress,
    int? ExpectedProgress,
    bool HasChildren);

/// <summary>遅れ・超過の判定（要件定義書 7.4、詳細設計書 4.4）。</summary>
public static class DelayEvaluator
{
    /// <summary>進捗遅れのしきい値の初期値（ポイント）。設定 Delay:ProgressLagThreshold で変えられる。</summary>
    public const int DefaultProgressLagThreshold = 20;

    public static IReadOnlyList<DelayFlag> Evaluate(DelayInput task, DateOnly today, int progressLagThreshold = DefaultProgressLagThreshold)
    {
        // 中止したタスクは予実の集計に含めないため、印も付けない
        if (task.Status == TaskItemStatus.Cancelled)
        {
            return [];
        }

        var flags = new List<DelayFlag>(2);

        if (task.PlannedEnd is { } end && end < today && task.Status != TaskItemStatus.Done)
        {
            flags.Add(DelayFlag.Overdue);
        }

        if (task.PlannedStart is { } start && start < today && task.Status == TaskItemStatus.NotStarted)
        {
            flags.Add(DelayFlag.LateStart);
        }

        if (task.PlannedMinutes is > 0 and var planned && task.ActualMinutes > planned)
        {
            flags.Add(DelayFlag.EffortOverrun);
        }

        if (!task.HasChildren
            && task.Status is TaskItemStatus.NotStarted or TaskItemStatus.InProgress
            && task.ExpectedProgress is { } expected
            && expected - task.Progress >= progressLagThreshold)
        {
            flags.Add(DelayFlag.ProgressLag);
        }

        return flags;
    }
}
