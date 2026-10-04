using Microsoft.Extensions.Options;

namespace TaskYojitsu.Application.Common;

/// <summary>
/// 時刻と業務上の「今日」（日本時間）。時刻は TimeProvider だけから得る（テストでは固定できる。詳細設計書 2.2）。
/// </summary>
public sealed class BusinessClock(TimeProvider timeProvider, IOptions<AppOptions> options)
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public TimeProvider TimeProvider => timeProvider;

    /// <summary>現在の日時（UTC）。</summary>
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>業務上の今日。</summary>
    public DateOnly Today => DateOnly.FromDateTime(ToLocal(UtcNow));

    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>UTC の日時を日本時間にする。</summary>
    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _timeZone);

    /// <summary>今週の月曜日（週は月曜始まり）。</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
