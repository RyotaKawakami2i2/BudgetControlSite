using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Domain.Tests;

/// <summary>稼働日と期待進捗（詳細設計書 4.3）。2026-10-05 は月曜日。</summary>
public class WorkingCalendarTests
{
    private static readonly DateOnly Mon = new(2026, 10, 5);

    [Fact]
    public void CountWorkingDays_平日だけを両端を含めて数える()
    {
        var calendar = WorkingCalendar.Empty;

        Assert.Equal(5, calendar.CountWorkingDays(Mon, Mon.AddDays(4)));
        Assert.Equal(5, calendar.CountWorkingDays(Mon, Mon.AddDays(6)));
        Assert.Equal(10, calendar.CountWorkingDays(Mon, Mon.AddDays(13)));
        Assert.Equal(1, calendar.CountWorkingDays(Mon, Mon));
        Assert.Equal(0, calendar.CountWorkingDays(Mon.AddDays(5), Mon.AddDays(6)));
    }

    [Fact]
    public void CountWorkingDays_終了日が開始日より前なら0()
    {
        Assert.Equal(0, WorkingCalendar.Empty.CountWorkingDays(Mon.AddDays(1), Mon));
    }

    [Fact]
    public void CountWorkingDays_平日の祝日を除き_土日の祝日は二重に引かない()
    {
        var calendar = new WorkingCalendar([Mon.AddDays(2), Mon.AddDays(5), Mon.AddDays(2)]);

        Assert.Equal(4, calendar.CountWorkingDays(Mon, Mon.AddDays(6)));
        Assert.False(calendar.IsWorkingDay(Mon.AddDays(2)));
        Assert.True(calendar.IsWorkingDay(Mon.AddDays(3)));
    }

    [Fact]
    public void CountWorkingDays_1日ずつ数えた結果と一致する()
    {
        var holidays = new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 12), new DateOnly(2026, 2, 11), new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 6) };
        var calendar = new WorkingCalendar(holidays);
        var start = new DateOnly(2025, 12, 20);
        for (var from = 0; from < 40; from += 3)
        {
            for (var length = 0; length < 200; length += 7)
            {
                var a = start.AddDays(from);
                var b = a.AddDays(length);
                var expected = 0;
                for (var d = a; d <= b; d = d.AddDays(1))
                {
                    if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(d))
                    {
                        expected++;
                    }
                }

                Assert.Equal(expected, calendar.CountWorkingDays(a, b));
            }
        }
    }

    [Fact]
    public void ExpectedProgress_予定日がなければ計算しない()
    {
        Assert.Null(WorkingCalendar.Empty.ExpectedProgress(null, Mon, Mon));
        Assert.Null(WorkingCalendar.Empty.ExpectedProgress(Mon, null, Mon));
    }

    [Fact]
    public void ExpectedProgress_開始日以前は0_終了日を過ぎたら100()
    {
        var calendar = WorkingCalendar.Empty;

        Assert.Equal(0, calendar.ExpectedProgress(Mon, Mon.AddDays(4), Mon.AddDays(-1)));
        Assert.Equal(0, calendar.ExpectedProgress(Mon, Mon.AddDays(4), Mon));
        Assert.Equal(100, calendar.ExpectedProgress(Mon, Mon.AddDays(4), Mon.AddDays(5)));
    }

    [Fact]
    public void ExpectedProgress_昨日までの稼働日の割合で_小数点以下は切り捨て()
    {
        var calendar = WorkingCalendar.Empty;

        // 月〜金の 5 日のうち、水曜日の時点で 2 日分
        Assert.Equal(40, calendar.ExpectedProgress(Mon, Mon.AddDays(4), Mon.AddDays(2)));
        // 月〜水の 3 日のうち 1 日分（33.3...）
        Assert.Equal(33, calendar.ExpectedProgress(Mon, Mon.AddDays(2), Mon.AddDays(1)));
        // 終了日の当日はまだ 100 にならない
        Assert.Equal(80, calendar.ExpectedProgress(Mon, Mon.AddDays(4), Mon.AddDays(4)));
    }

    [Fact]
    public void ExpectedProgress_祝日は数えない()
    {
        // 火曜日が祝日。月〜金の稼働日は 4 日で、木曜日の時点で 2 日分
        var calendar = new WorkingCalendar([Mon.AddDays(1)]);

        Assert.Equal(50, calendar.ExpectedProgress(Mon, Mon.AddDays(4), Mon.AddDays(3)));
    }

    [Fact]
    public void ExpectedProgress_予定期間がすべて休日なら暦日で計算する()
    {
        var saturday = Mon.AddDays(5);

        Assert.Equal(50, WorkingCalendar.Empty.ExpectedProgress(saturday, saturday.AddDays(1), saturday.AddDays(1)));
    }
}
