namespace TaskYojitsu.Domain.Rules;

/// <summary>入力の上限と範囲（要件定義書 7.5、詳細設計書 4.6）。DB の制約と同じ値。</summary>
public static class Limits
{
    public const int TaskTitleMax = 200;
    public const int TaskTextMax = 4000;
    public const int PlannedMinutesMax = 599_940; // 9,999 時間
    public const int MinuteStep = 15;
    public const int ProgressStep = 5;
    public const int TagsPerTaskMax = 20;

    public const int WorkLogMinutesMin = 15;
    public const int WorkLogMinutesMax = 1440;
    public const int DailyMinutesMax = 1440;
    public const int WorkLogNoteMax = 500;

    public const int TeamNameMax = 50;
    public const int TeamDescriptionMax = 500;
    public const int TagNameMax = 30;
    public const int CommentMax = 2000;
    public const int ViewNameMax = 50;
    public const int ViewConditionsMaxBytes = 4096;
    public const int DisplayNameMax = 50;
    public const int EmailMax = 254;

    public static readonly DateOnly MinDate = new(2000, 1, 1);
    public static readonly DateOnly MaxDate = new(2099, 12, 31);

    public static bool IsMinuteStep(int minutes) => minutes % MinuteStep == 0;
}
