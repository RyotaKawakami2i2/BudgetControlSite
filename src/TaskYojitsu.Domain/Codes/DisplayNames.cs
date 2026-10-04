using System.Globalization;

namespace TaskYojitsu.Domain.Codes;

/// <summary>
/// 区分値の画面の表示名（基本設計書 5.3）。サーバー側で表示名が要るのは、変更履歴とメールだけ。
/// 画面（TypeScript）側は web/src/lib/labels.ts に同じ対応を持つ。
/// </summary>
public static class DisplayNames
{
    public static string Of(TaskItemStatus status) => status switch
    {
        TaskItemStatus.NotStarted => "未着手",
        TaskItemStatus.InProgress => "進行中",
        TaskItemStatus.OnHold => "保留",
        TaskItemStatus.Done => "完了",
        TaskItemStatus.Cancelled => "中止",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string Of(Priority priority) => priority switch
    {
        Priority.High => "高",
        Priority.Medium => "中",
        Priority.Low => "低",
        _ => throw new ArgumentOutOfRangeException(nameof(priority)),
    };

    public static string Of(TeamRole role) => role switch
    {
        TeamRole.Leader => "リーダー",
        TeamRole.Member => "メンバー",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static string Of(UserStatus status) => status switch
    {
        UserStatus.Invited => "招待中",
        UserStatus.Active => "有効",
        UserStatus.Disabled => "無効",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>日付を「2026/10/03」の形式にする（変更履歴用）。</summary>
    public static string Date(DateOnly? date) =>
        date is { } d ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "";

    /// <summary>工数（分）を「1.5h」の形式にする。</summary>
    public static string Hours(int? minutes) =>
        minutes is { } m ? (m / 60m).ToString("0.##", CultureInfo.InvariantCulture) + "h" : "";
}
