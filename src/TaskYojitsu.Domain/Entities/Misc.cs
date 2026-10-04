using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Entities;

/// <summary>ビュー（saved_views）。ガントの表示条件に名前を付けて保存したもの。</summary>
public class SavedView
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }

    /// <summary>共有ビューのチーム。</summary>
    public Guid? TeamId { get; set; }

    public bool IsShared { get; set; }
    public string Name { get; set; } = "";

    /// <summary>表示条件（JSON。詳細設計書 7.3.6）。</summary>
    public string Conditions { get; set; } = "{}";

    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>通知（notifications）。内容は保存せず、表示のたびに権限を確かめて読み出す。</summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationKind Kind { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? ActorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

/// <summary>祝日（holidays）。</summary>
public class Holiday
{
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>
/// 監査ログ（audit_logs）。追記だけを許す。prev_hash と hash は DB のトリガーが設定する（詳細設計書 3.5）。
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? ActorId { get; set; }

    /// <summary>未ログインで入力されたメールアドレスの HMAC の値（平文は残さない）。</summary>
    public string? ActorHint { get; set; }

    public string Action { get; set; } = "";
    public AuditResult Result { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public Guid? TeamId { get; set; }
    public System.Net.IPAddress? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }

    /// <summary>変更前後の値など（JSON）。秘密情報は入れない。</summary>
    public string? Detail { get; set; }

    public byte[]? PrevHash { get; set; }
    public byte[]? Hash { get; set; }
}
