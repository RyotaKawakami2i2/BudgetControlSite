namespace TaskYojitsu.Domain.Codes;

// 区分値（基本設計書 5.3）。DB と API には、名前を snake_case にしたコードで保存・送受信する（EnumCodes）。

/// <summary>タスクの状態。</summary>
public enum TaskItemStatus
{
    NotStarted,
    InProgress,
    OnHold,
    Done,
    Cancelled,
}

/// <summary>優先度。</summary>
public enum Priority
{
    High,
    Medium,
    Low,
}

/// <summary>チームでの役割。</summary>
public enum TeamRole
{
    Leader,
    Member,
}

/// <summary>利用者の状態。</summary>
public enum UserStatus
{
    Invited,
    Active,
    Disabled,
}

/// <summary>認証の方式。</summary>
public enum AuthMethod
{
    Password,
    PasswordTotp,
    PasswordRecovery,
    Passkey,
}

/// <summary>通知の種類。</summary>
public enum NotificationKind
{
    TaskAssigned,
    TaskUnassigned,
    CommentAdded,
    DueTomorrow,
    Overdue,
    TeamAdded,
}

/// <summary>タグの色（背景とのコントラスト比を確認した 8 色）。</summary>
public enum TagColor
{
    Gray,
    Blue,
    Green,
    Yellow,
    Orange,
    Red,
    Purple,
    Teal,
}

/// <summary>作業実績を記録した画面。</summary>
public enum WorkLogSource
{
    Dialog,
    Timesheet,
}

/// <summary>変更履歴の種類。</summary>
public enum TaskHistoryKind
{
    Created,
    Updated,
    Moved,
    Deleted,
    Restored,
    WorklogAdded,
    WorklogUpdated,
    WorklogDeleted,
    DependencyAdded,
    DependencyRemoved,
}

/// <summary>遅れ・超過の印（詳細設計書 4.4）。</summary>
public enum DelayFlag
{
    Overdue,
    LateStart,
    EffortOverrun,
    ProgressLag,
}

/// <summary>セッションを失効させた理由。</summary>
public enum SessionRevokeReason
{
    Logout,
    PasswordChanged,
    MfaChanged,
    Disabled,
    UserRevoked,
    AdminRevoked,
}

/// <summary>監査ログの結果。</summary>
public enum AuditResult
{
    Success,
    Failure,
    Denied,
}
