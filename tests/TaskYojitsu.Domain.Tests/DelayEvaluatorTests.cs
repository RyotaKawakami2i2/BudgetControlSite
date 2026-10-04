using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Domain.Tests;

/// <summary>遅れ・超過の判定（詳細設計書 4.4）。</summary>
public class DelayEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static DelayInput Task(
        TaskItemStatus status = TaskItemStatus.InProgress,
        DateOnly? start = null,
        DateOnly? end = null,
        int? planned = null,
        int actual = 0,
        int progress = 0,
        int? expected = null,
        bool hasChildren = false) =>
        new(status, start, end, planned, actual, progress, expected, hasChildren);

    [Theory]
    [InlineData(TaskItemStatus.NotStarted, true)]
    [InlineData(TaskItemStatus.InProgress, true)]
    [InlineData(TaskItemStatus.OnHold, true)]
    [InlineData(TaskItemStatus.Done, false)]
    [InlineData(TaskItemStatus.Cancelled, false)]
    public void Overdue_予定終了日を過ぎて完了も中止もしていない(TaskItemStatus status, bool expected)
    {
        var flags = DelayEvaluator.Evaluate(Task(status, Today.AddDays(-5), Today.AddDays(-1)), Today);

        Assert.Equal(expected, flags.Contains(DelayFlag.Overdue));
    }

    [Fact]
    public void Overdue_予定終了日の当日はまだ付かない()
    {
        Assert.DoesNotContain(DelayFlag.Overdue, DelayEvaluator.Evaluate(Task(end: Today, start: Today), Today));
    }

    [Fact]
    public void LateStart_予定開始日を過ぎても未着手()
    {
        var notStarted = DelayEvaluator.Evaluate(Task(TaskItemStatus.NotStarted, Today.AddDays(-1), Today.AddDays(3)), Today);
        var started = DelayEvaluator.Evaluate(Task(TaskItemStatus.InProgress, Today.AddDays(-1), Today.AddDays(3)), Today);
        var startsToday = DelayEvaluator.Evaluate(Task(TaskItemStatus.NotStarted, Today, Today.AddDays(3)), Today);

        Assert.Contains(DelayFlag.LateStart, notStarted);
        Assert.DoesNotContain(DelayFlag.LateStart, started);
        Assert.DoesNotContain(DelayFlag.LateStart, startsToday);
    }

    [Fact]
    public void Milestone_過ぎたら期限超過と開始遅れ()
    {
        var day = Today.AddDays(-2);

        var flags = DelayEvaluator.Evaluate(Task(TaskItemStatus.NotStarted, day, day), Today);

        Assert.Equal([DelayFlag.Overdue, DelayFlag.LateStart], flags);
    }

    [Theory]
    [InlineData(60, 75, true)]
    [InlineData(60, 60, false)]
    [InlineData(0, 15, false)]
    [InlineData(null, 600, false)]
    public void EffortOverrun_実績工数が予定工数を超えた(int? planned, int actual, bool expected)
    {
        var flags = DelayEvaluator.Evaluate(Task(planned: planned, actual: actual), Today);

        Assert.Equal(expected, flags.Contains(DelayFlag.EffortOverrun));
    }

    [Theory]
    [InlineData(TaskItemStatus.InProgress, 50, 30, false, true)]
    [InlineData(TaskItemStatus.NotStarted, 20, 0, false, true)]
    [InlineData(TaskItemStatus.InProgress, 50, 35, false, false)]
    [InlineData(TaskItemStatus.OnHold, 80, 0, false, false)]
    [InlineData(TaskItemStatus.InProgress, 80, 0, true, false)]
    public void ProgressLag_期待進捗との差がしきい値以上(TaskItemStatus status, int expectedProgress, int progress, bool hasChildren, bool expected)
    {
        var flags = DelayEvaluator.Evaluate(
            Task(status, Today.AddDays(-5), Today.AddDays(5), progress: progress, expected: expectedProgress, hasChildren: hasChildren), Today);

        Assert.Equal(expected, flags.Contains(DelayFlag.ProgressLag));
    }

    [Fact]
    public void ProgressLag_しきい値は設定で変えられる()
    {
        var task = Task(start: Today.AddDays(-5), end: Today.AddDays(5), progress: 40, expected: 50);

        Assert.DoesNotContain(DelayFlag.ProgressLag, DelayEvaluator.Evaluate(task, Today));
        Assert.Contains(DelayFlag.ProgressLag, DelayEvaluator.Evaluate(task, Today, progressLagThreshold: 10));
    }

    [Fact]
    public void Cancelled_中止したタスクには印を付けない()
    {
        var task = Task(TaskItemStatus.Cancelled, Today.AddDays(-9), Today.AddDays(-1), planned: 60, actual: 120, expected: 100);

        Assert.Empty(DelayEvaluator.Evaluate(task, Today));
    }
}
