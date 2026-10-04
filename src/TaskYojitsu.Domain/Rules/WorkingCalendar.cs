namespace TaskYojitsu.Domain.Rules;

/// <summary>
/// 稼働日の計算と期待進捗（詳細設計書 4.3）。稼働日は、土曜日・日曜日と祝日を除いた日。
/// </summary>
public sealed class WorkingCalendar
{
    // 平日の祝日だけを、日付の順に持つ（範囲内の件数を二分探索で数えるため）
    private readonly int[] _weekdayHolidays;

    public WorkingCalendar(IEnumerable<DateOnly> holidays)
    {
        _weekdayHolidays = [.. holidays
            .Where(d => !IsWeekend(d))
            .Select(d => d.DayNumber)
            .Distinct()
            .Order()];
    }

    public static WorkingCalendar Empty { get; } = new([]);

    public static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    public bool IsWorkingDay(DateOnly date) =>
        !IsWeekend(date) && Array.BinarySearch(_weekdayHolidays, date.DayNumber) < 0;

    /// <summary>from から to まで（両端を含む）の稼働日の数。to が from より前なら 0。</summary>
    public int CountWorkingDays(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0;
        }

        var total = to.DayNumber - from.DayNumber + 1;
        var weeks = total / 7;
        var count = weeks * 5;
        var cursor = from.AddDays(weeks * 7);
        for (var i = 0; i < total % 7; i++)
        {
            if (!IsWeekend(cursor.AddDays(i)))
            {
                count++;
            }
        }

        return count - CountHolidays(from.DayNumber, to.DayNumber);
    }

    /// <summary>
    /// 期待進捗（0〜100）。「昨日までに終わっているはずの割合」を表す。予定日がなければ null。
    /// </summary>
    public int? ExpectedProgress(DateOnly? plannedStart, DateOnly? plannedEnd, DateOnly today)
    {
        if (plannedStart is not { } start || plannedEnd is not { } end)
        {
            return null;
        }

        if (today <= start)
        {
            return 0;
        }

        if (today > end)
        {
            return 100;
        }

        var yesterday = today.AddDays(-1);
        long elapsed = CountWorkingDays(start, yesterday);
        long whole = CountWorkingDays(start, end);
        if (whole == 0)
        {
            // 予定期間がすべて休日の場合は、暦日の日数で計算する
            elapsed = yesterday.DayNumber - start.DayNumber + 1;
            whole = end.DayNumber - start.DayNumber + 1;
        }

        return (int)Math.Clamp(elapsed * 100 / whole, 0, 100);
    }

    private int CountHolidays(int fromDay, int toDay)
    {
        var lower = LowerBound(fromDay);
        var upper = LowerBound(toDay + 1);
        return upper - lower;
    }

    private int LowerBound(int value)
    {
        var index = Array.BinarySearch(_weekdayHolidays, value);
        return index >= 0 ? index : ~index;
    }
}
