using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Application.Tasks;

/// <summary>画面に出す操作の可否（ボタンの表示に使う。判定はサーバーで毎回行う）。</summary>
public sealed record TaskCan(bool EditPlan, bool Assign, bool EditActual, bool LogWork, bool AddChild, bool Delete)
{
    public static TaskCan None { get; } = new(false, false, false, false, false, false);
}

/// <summary>ガントの 1 行分のタスク（詳細設計書 5.4.1）。説明と結果コメントは含めない。</summary>
public sealed record GanttTaskDto(
    Guid Id,
    Guid TeamId,
    Guid? ParentId,
    int Depth,
    int SortOrder,
    string Title,
    Guid? AssigneeId,
    Guid CreatedById,
    TaskItemStatus Status,
    Priority Priority,
    bool IsMilestone,
    bool IsSummary,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    DateOnly? ActualStart,
    DateOnly? ActualEnd,
    int ActualMinutes,
    int Progress,
    int? ExpectedProgress,
    IReadOnlyList<DelayFlag> Flags,
    bool DescendantFlagged,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<Guid> PredecessorIds,
    int Version,
    TaskCan Can);

public sealed record TaskRef(Guid Id, string Title);

/// <summary>タスクの選択肢（親タスク・先行タスクの選択に使う）。</summary>
public sealed record TaskOptionDto(
    Guid Id,
    Guid? ParentId,
    int Depth,
    string Title,
    bool IsMilestone,
    TaskItemStatus Status,
    bool HasChildren,
    Guid? AssigneeId,
    Guid CreatedById);

public sealed record TagDto(Guid Id, Guid TeamId, string Name, TagColor Color);

public sealed record DependencyDto(Guid Id, string Title, DateOnly? PlannedStart, DateOnly? PlannedEnd, TaskItemStatus Status);

/// <summary>タスクの詳細で使う、追加の操作の可否。</summary>
public sealed record TaskDetailCan(
    bool EditPlan,
    bool Assign,
    bool EditActual,
    bool LogWork,
    bool AddChild,
    bool Delete,
    bool Move,
    bool EditDependencies,
    bool Comment,
    bool ViewAllWorkLogs,
    IReadOnlyList<TaskItemStatus> NextStatuses);

/// <summary>タスクの詳細（API-21）。</summary>
public sealed record TaskDetailDto(
    GanttTaskDto Task,
    string? Description,
    string? ResultNote,
    string TeamName,
    bool TeamArchived,
    string Role,
    IReadOnlyList<TaskRef> Path,
    string? AssigneeName,
    string CreatedByName,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<DependencyDto> Predecessors,
    IReadOnlyList<DependencyDto> Successors,
    int ChildCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    TaskDetailCan Can,
    IReadOnlyList<string> Warnings);

/// <summary>タスクの登録（API-22。詳細設計書 5.4.2）。</summary>
public sealed record CreateTaskRequest(
    Guid TeamId,
    Guid? ParentId,
    string? Title,
    string? Description,
    Guid? AssigneeId,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    int? PlannedMinutes,
    string? Priority,
    IReadOnlyList<Guid>? TagIds,
    bool IsMilestone);

/// <summary>タスクの変更（API-23。詳細設計書 5.4.3）。送られた項目だけを変える。</summary>
public sealed record UpdateTaskRequest
{
    public int Version { get; init; }
    public Optional<string?> Title { get; init; }
    public Optional<string?> Description { get; init; }
    public Optional<Guid?> AssigneeId { get; init; }
    public Optional<DateOnly?> PlannedStart { get; init; }
    public Optional<DateOnly?> PlannedEnd { get; init; }
    public Optional<int?> PlannedMinutes { get; init; }
    public Optional<string?> Priority { get; init; }
    public Optional<IReadOnlyList<Guid>?> TagIds { get; init; }
    public Optional<bool?> IsMilestone { get; init; }
    public Optional<string?> Status { get; init; }
    public Optional<int?> Progress { get; init; }
    public Optional<DateOnly?> ActualStart { get; init; }
    public Optional<DateOnly?> ActualEnd { get; init; }
    public Optional<string?> ResultNote { get; init; }
}

/// <summary>親と並び順の変更（API-24）。afterTaskId の直後に置く（null なら先頭）。</summary>
public sealed record MoveTaskRequest(int Version, Guid? ParentId, Guid? AfterTaskId);

/// <summary>削除したタスク（API-26）。</summary>
public sealed record DeletedTaskDto(Guid Id, string Title, Guid? ParentId, DateTime DeletedAt, string? DeletedByName, int DescendantCount, bool CanRestore);

/// <summary>変更履歴の 1 件（API-28）。</summary>
public sealed record TaskHistoryDto(
    long Id,
    DateTime OccurredAt,
    Guid ActorId,
    string ActorName,
    TaskHistoryKind Kind,
    string? Field,
    string? OldValue,
    string? NewValue);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
