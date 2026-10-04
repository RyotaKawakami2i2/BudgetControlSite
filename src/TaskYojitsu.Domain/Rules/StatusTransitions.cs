using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Rules;

/// <summary>状態を変える人の立場。</summary>
/// <param name="IsLeader">チームのリーダー。</param>
/// <param name="IsAssignee">タスクの担当者。</param>
/// <param name="IsCreator">タスクの作成者。</param>
public readonly record struct TransitionActor(bool IsLeader, bool IsAssignee, bool IsCreator);

/// <summary>タスクの状態の変化（要件定義書 7.5、詳細設計書 4.1）。表にない変化は受け付けない。</summary>
public static class StatusTransitions
{
    private enum Who
    {
        /// <summary>担当者かリーダー。</summary>
        AssigneeOrLeader,

        /// <summary>リーダー、または自分が作成して担当しているタスクの担当者。</summary>
        OwnerOrLeader,

        /// <summary>リーダーだけ。</summary>
        LeaderOnly,
    }

    private static readonly Dictionary<(TaskItemStatus From, TaskItemStatus To), Who> Allowed = new()
    {
        [(TaskItemStatus.NotStarted, TaskItemStatus.InProgress)] = Who.AssigneeOrLeader,
        [(TaskItemStatus.NotStarted, TaskItemStatus.Done)] = Who.AssigneeOrLeader,
        [(TaskItemStatus.InProgress, TaskItemStatus.Done)] = Who.AssigneeOrLeader,
        [(TaskItemStatus.Done, TaskItemStatus.InProgress)] = Who.AssigneeOrLeader,
        [(TaskItemStatus.NotStarted, TaskItemStatus.OnHold)] = Who.OwnerOrLeader,
        [(TaskItemStatus.InProgress, TaskItemStatus.OnHold)] = Who.OwnerOrLeader,
        [(TaskItemStatus.OnHold, TaskItemStatus.InProgress)] = Who.OwnerOrLeader,
        [(TaskItemStatus.OnHold, TaskItemStatus.NotStarted)] = Who.OwnerOrLeader,
        [(TaskItemStatus.NotStarted, TaskItemStatus.Cancelled)] = Who.OwnerOrLeader,
        [(TaskItemStatus.InProgress, TaskItemStatus.Cancelled)] = Who.OwnerOrLeader,
        [(TaskItemStatus.OnHold, TaskItemStatus.Cancelled)] = Who.OwnerOrLeader,
        [(TaskItemStatus.Cancelled, TaskItemStatus.NotStarted)] = Who.LeaderOnly,
    };

    /// <summary>状態の変化が表にあるか（同じ状態のままは常に可）。</summary>
    public static bool IsDefined(TaskItemStatus from, TaskItemStatus to) =>
        from == to || Allowed.ContainsKey((from, to));

    /// <summary>その人がその変化を行えるか。</summary>
    public static bool CanPerform(TaskItemStatus from, TaskItemStatus to, TransitionActor actor)
    {
        if (from == to)
        {
            return true;
        }

        if (!Allowed.TryGetValue((from, to), out var who))
        {
            return false;
        }

        return who switch
        {
            Who.AssigneeOrLeader => actor.IsLeader || actor.IsAssignee,
            Who.OwnerOrLeader => actor.IsLeader || (actor.IsAssignee && actor.IsCreator),
            Who.LeaderOnly => actor.IsLeader,
            _ => false,
        };
    }

    /// <summary>次に選べる状態の一覧（画面の選択肢に使う）。</summary>
    public static IReadOnlyList<TaskItemStatus> NextStatuses(TaskItemStatus from, TransitionActor actor) =>
        [.. Enum.GetValues<TaskItemStatus>().Where(to => to != from && CanPerform(from, to, actor))];
}
