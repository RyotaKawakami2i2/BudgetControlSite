using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Entities;

/// <summary>
/// タスク（tasks）。.NET の Task 型と名前がぶつからないように TaskItem とする。
/// 子を持つタスク（まとめタスク）の日程・工数・進捗・状態は、表示のたびに子から計算する（RollupCalculator）。
/// </summary>
public class TaskItem
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ParentId { get; set; }
    public short Depth { get; set; } = 1;
    public int SortOrder { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid CreatedBy { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.NotStarted;
    public Priority Priority { get; set; } = Priority.Medium;
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public int? PlannedMinutes { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }
    public short Progress { get; set; }
    public bool IsMilestone { get; set; }
    public string? ResultNote { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    /// <summary>同時に削除した子孫に同じ値を入れる（復元の単位）。</summary>
    public Guid? DeleteBatchId { get; set; }
}

/// <summary>依存関係（task_dependencies）。先行タスクが終わってから後続タスクを始める。</summary>
public class TaskDependency
{
    public Guid TeamId { get; set; }
    public Guid PredecessorId { get; set; }
    public Guid SuccessorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

/// <summary>タスクとタグの対応（task_tags）。</summary>
public class TaskTag
{
    public Guid TeamId { get; set; }
    public Guid TaskId { get; set; }
    public Guid TagId { get; set; }
}

/// <summary>変更履歴（task_histories）。チームの利用者が見る。</summary>
public class TaskHistory
{
    public long Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid TeamId { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid ActorId { get; set; }
    public TaskHistoryKind Kind { get; set; }

    /// <summary>変更した項目（updated のとき）。</summary>
    public string? Field { get; set; }

    /// <summary>変更前の値（表示用の文字列）。</summary>
    public string? OldValue { get; set; }

    /// <summary>変更後の値（表示用の文字列）。</summary>
    public string? NewValue { get; set; }
}

/// <summary>コメント（comments）。本文はプレーンテキスト。</summary>
public class Comment
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid TaskId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>作業実績（work_logs）。実績工数は、この合計だけから求める（DATA-01）。</summary>
public class WorkLog
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly WorkDate { get; set; }
    public int Minutes { get; set; }
    public string? Note { get; set; }
    public WorkLogSource Source { get; set; } = WorkLogSource.Dialog;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
