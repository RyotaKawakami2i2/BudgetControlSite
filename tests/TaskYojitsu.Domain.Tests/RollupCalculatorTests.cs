using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Domain.Tests;

/// <summary>まとめタスクの集計（詳細設計書 4.2）。2026-10-05 は月曜日。</summary>
public class RollupCalculatorTests
{
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static readonly Guid Root = Guid.CreateVersion7();

    private static RollupInput Child(
        TaskItemStatus status = TaskItemStatus.NotStarted,
        DateOnly? start = null,
        DateOnly? end = null,
        int? planned = null,
        int progress = 0,
        int actual = 0,
        DateOnly? actualStart = null,
        DateOnly? actualEnd = null,
        Guid? parent = null) =>
        new(Guid.CreateVersion7(), parent ?? Root, status, start, end, planned, actualStart, actualEnd, progress, actual);

    private static RollupInput RootTask(int ownActual = 0, DateOnly? actualStart = null) =>
        new(Root, null, TaskItemStatus.NotStarted, null, null, null, actualStart, null, 0, ownActual);

    private static RollupResult Calculate(DateOnly today, params RollupInput[] tasks) =>
        RollupCalculator.Calculate(tasks, WorkingCalendar.Empty, today)[Root];

    [Fact]
    public void 子を持たないタスクは自分の値をそのまま使う()
    {
        var leaf = RootTask(ownActual: 90);

        var result = Calculate(Mon, leaf);

        Assert.False(result.IsSummary);
        Assert.Equal(90, result.ActualMinutes);
        Assert.Null(result.ExpectedProgress);
    }

    [Fact]
    public void 日程は子の最も早い開始日と最も遅い終了日()
    {
        var result = Calculate(Mon,
            RootTask(),
            Child(start: Mon.AddDays(2), end: Mon.AddDays(3)),
            Child(start: Mon, end: Mon.AddDays(1)),
            Child());

        Assert.True(result.IsSummary);
        Assert.Equal(Mon, result.PlannedStart);
        Assert.Equal(Mon.AddDays(3), result.PlannedEnd);
    }

    [Fact]
    public void 予定工数は子の合計で_空は0とし_すべて空なら空()
    {
        Assert.Equal(90, Calculate(Mon, RootTask(), Child(planned: 90), Child()).PlannedMinutes);
        Assert.Null(Calculate(Mon, RootTask(), Child(), Child()).PlannedMinutes);
    }

    [Fact]
    public void 実績工数は自分の記録と子の合計()
    {
        var result = Calculate(Mon, RootTask(ownActual: 30), Child(actual: 60), Child(actual: 15));

        Assert.Equal(105, result.ActualMinutes);
    }

    [Fact]
    public void 実績開始日は子と自分のうち最も早い日()
    {
        var result = Calculate(Mon,
            RootTask(actualStart: Mon.AddDays(-3)),
            Child(TaskItemStatus.InProgress, actualStart: Mon.AddDays(-1)));

        Assert.Equal(Mon.AddDays(-3), result.ActualStart);
    }

    [Fact]
    public void 状態_すべて完了なら完了で_実績終了日は最も遅い日()
    {
        var result = Calculate(Mon,
            RootTask(),
            Child(TaskItemStatus.Done, progress: 100, actualStart: Mon.AddDays(-4), actualEnd: Mon.AddDays(-2)),
            Child(TaskItemStatus.Done, progress: 100, actualStart: Mon.AddDays(-4), actualEnd: Mon.AddDays(-1)));

        Assert.Equal(TaskItemStatus.Done, result.Status);
        Assert.Equal(Mon.AddDays(-1), result.ActualEnd);
        Assert.Equal(100, result.Progress);
    }

    [Theory]
    [InlineData(TaskItemStatus.NotStarted, TaskItemStatus.NotStarted, TaskItemStatus.NotStarted)]
    [InlineData(TaskItemStatus.NotStarted, TaskItemStatus.Done, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.OnHold, TaskItemStatus.NotStarted, TaskItemStatus.InProgress)]
    [InlineData(TaskItemStatus.Done, TaskItemStatus.Cancelled, TaskItemStatus.Done)]
    [InlineData(TaskItemStatus.Cancelled, TaskItemStatus.Cancelled, TaskItemStatus.Cancelled)]
    public void 状態は子から決まり_中止した子は除く(TaskItemStatus a, TaskItemStatus b, TaskItemStatus expected)
    {
        var result = Calculate(Mon, RootTask(), Child(a), Child(b));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void 未完了の子があれば実績終了日は空()
    {
        var result = Calculate(Mon,
            RootTask(),
            Child(TaskItemStatus.Done, progress: 100, actualEnd: Mon.AddDays(-1)),
            Child(TaskItemStatus.InProgress, progress: 50));

        Assert.Null(result.ActualEnd);
    }

    [Fact]
    public void 中止した子は工数と日程の計算から除く()
    {
        var result = Calculate(Mon,
            RootTask(),
            Child(start: Mon, end: Mon.AddDays(1), planned: 60, actual: 15),
            Child(TaskItemStatus.Cancelled, start: Mon.AddDays(-9), end: Mon.AddDays(9), planned: 600, actual: 30));

        Assert.Equal(60, result.PlannedMinutes);
        Assert.Equal(15, result.ActualMinutes);
        Assert.Equal(Mon, result.PlannedStart);
        Assert.Equal(Mon.AddDays(1), result.PlannedEnd);
    }

    [Fact]
    public void 進捗率は予定工数で重み付けした平均で_小数点以下は切り捨て()
    {
        // (60×100 + 120×0 + 0) ÷ 180 = 33.3...
        var result = Calculate(Mon,
            RootTask(),
            Child(TaskItemStatus.Done, planned: 60, progress: 100),
            Child(planned: 120, progress: 0));

        Assert.Equal(33, result.Progress);
    }

    [Fact]
    public void 予定工数がない子の重みは稼働日数で_それもなければ1()
    {
        // 重み: 予定期間の稼働日数 5 の子と、日程もない（重み 1）子
        var withDays = Calculate(Mon,
            RootTask(),
            Child(start: Mon, end: Mon.AddDays(4), progress: 100),
            Child(progress: 100));

        // (5×100 + 1×100) ÷ 6 = 100
        Assert.Equal(100, withDays.Progress);

        var mixed = Calculate(Mon,
            RootTask(),
            Child(start: Mon, end: Mon.AddDays(4), progress: 50),
            Child(progress: 0));

        // (5×50 + 1×0) ÷ 6 = 41.6...
        Assert.Equal(41, mixed.Progress);
    }

    [Fact]
    public void 子孫に遅れがあれば印を付ける()
    {
        var middle = Child(TaskItemStatus.InProgress);
        var grandChild = Child(TaskItemStatus.InProgress, Mon.AddDays(-5), Mon.AddDays(-1), parent: middle.Id);

        var results = RollupCalculator.Calculate([RootTask(), middle, grandChild], WorkingCalendar.Empty, Mon);

        Assert.Contains(DelayFlag.Overdue, results[grandChild.Id].Flags);
        Assert.True(results[middle.Id].DescendantFlagged);
        Assert.True(results[Root].DescendantFlagged);
        // まとめタスク自身の値（子から計算した日程）でも判定する
        Assert.Contains(DelayFlag.Overdue, results[Root].Flags);
    }

    [Fact]
    public void まとめタスクには進捗遅れの印を付けない()
    {
        var result = Calculate(Mon.AddDays(4),
            RootTask(),
            Child(TaskItemStatus.InProgress, Mon, Mon.AddDays(4), progress: 0, planned: 60));

        Assert.Equal(80, result.ExpectedProgress);
        Assert.DoesNotContain(DelayFlag.ProgressLag, result.Flags);
    }

    [Fact]
    public void 親が集計の対象にない場合は根として扱う()
    {
        var orphan = Child(TaskItemStatus.InProgress, parent: Guid.CreateVersion7(), actual: 45);

        var results = RollupCalculator.Calculate([orphan], WorkingCalendar.Empty, Mon);

        Assert.False(results[orphan.Id].IsSummary);
        Assert.Equal(45, results[orphan.Id].ActualMinutes);
    }
}
