namespace TaskYojitsu.Domain.Rules;

/// <summary>工数の計算（要件定義書 7.3、詳細設計書 7.3.7）。</summary>
public static class EffortRules
{
    /// <summary>
    /// 期間 [from, to] に入る予定工数（分）。予定工数を予定期間の稼働日数で割り、期間と重なる稼働日数を掛ける。
    /// 予定期間がすべて休日の場合は、暦日の日数で按分する。
    /// </summary>
    public static double ProratedMinutes(
        WorkingCalendar calendar, DateOnly? plannedStart, DateOnly? plannedEnd, int? plannedMinutes, DateOnly from, DateOnly to)
    {
        if (plannedStart is not { } start || plannedEnd is not { } end || plannedMinutes is not > 0 || end < from || start > to)
        {
            return 0;
        }

        var overlapFrom = start > from ? start : from;
        var overlapTo = end < to ? end : to;
        double whole = calendar.CountWorkingDays(start, end);
        double part = calendar.CountWorkingDays(overlapFrom, overlapTo);
        if (whole == 0)
        {
            whole = end.DayNumber - start.DayNumber + 1;
            part = overlapTo.DayNumber - overlapFrom.DayNumber + 1;
        }

        return plannedMinutes.Value * part / whole;
    }

    /// <summary>工数差（実績工数 − 予定工数。プラスは予定を超えている）。</summary>
    public static int? Variance(int? plannedMinutes, int actualMinutes) =>
        plannedMinutes is { } planned ? actualMinutes - planned : null;
}
