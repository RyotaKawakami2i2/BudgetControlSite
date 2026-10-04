using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Application.Security;

/// <summary>権限を判定する操作（詳細設計書 4.5）。</summary>
public enum Operation
{
    TeamView,
    TeamCreate,
    TeamUpdate,
    TeamArchive,
    TeamUnarchive,
    TeamMemberAdd,
    TeamMemberRemove,
    TeamLeaderAssign,
    TeamLeaderRevoke,
    TeamTagManage,
    TeamSharedViewManage,
    TaskView,
    TaskCreate,
    TaskPlanEdit,
    TaskAssign,
    TaskChildAdd,
    TaskMove,
    TaskDependencyEdit,
    TaskDelete,
    TaskRestore,
    TaskActualEdit,
    WorklogCreate,
    WorklogEdit,
    WorklogDelete,
    WorklogViewDetail,
    CommentCreate,
    CommentEdit,
    CommentDelete,
    ReportTeam,
}

/// <summary>判定の結果。</summary>
public enum AccessOutcome
{
    Allow,

    /// <summary>404（チームに所属していない。存在しない場合と同じ応答。NF-ACC-03）。</summary>
    NotFound,

    /// <summary>403（auth.forbidden）。</summary>
    Forbidden,

    /// <summary>403（team.archived）。</summary>
    Archived,

    /// <summary>403（auth.reauth_required）。管理者の権限による変更で、直近の再認証がない。</summary>
    ReauthRequired,
}

/// <summary>
/// 判定の材料（詳細設計書 4.5 の記号）。A: 管理者、R: 対象チームでの役割、C: 作成者、S: 担当者、
/// W: 作業実績がある、X: アーカイブ済み、L: いずれかのチームでリーダー。
/// </summary>
public sealed record AccessFacts
{
    public bool IsActiveUser { get; init; } = true;

    /// <summary>A</summary>
    public bool IsAdmin { get; init; }

    /// <summary>R（null は所属なし）</summary>
    public TeamRole? Role { get; init; }

    /// <summary>X</summary>
    public bool IsArchived { get; init; }

    /// <summary>L</summary>
    public bool IsLeaderAnywhere { get; init; }

    /// <summary>C（対象タスク）</summary>
    public bool IsCreator { get; init; }

    /// <summary>S（対象タスク）</summary>
    public bool IsAssignee { get; init; }

    /// <summary>W（対象タスクと子孫）</summary>
    public bool HasWorkLogs { get; init; }

    /// <summary>対象タスクと子孫のすべてを自分が作成したか（メンバーによる削除の条件）。</summary>
    public bool CreatedWholeSubtree { get; init; }

    /// <summary>
    /// もう一方のタスク（子の追加では親、移動では移動先の親、依存関係では相手）について C かつ S か。
    /// 移動先が最上位の場合は true とする。
    /// </summary>
    public bool OtherIsOwn { get; init; } = true;

    /// <summary>作業実績・コメントを記録した本人か。</summary>
    public bool IsRecordOwner { get; init; }

    /// <summary>管理者の操作の条件（直近 10 分以内の認証と、許可したネットワーク）を満たすか。</summary>
    public bool AdminStepUp { get; init; }
}

/// <summary>
/// 権限の判定表（詳細設計書 4.5）。判定はここだけで行い、API の処理には条件を書かない（NF-ACC-05）。
/// </summary>
public static class AccessRules
{
    // 管理者が、所属していないチームに対して行える操作（閲覧と、チームの管理）
    private static readonly HashSet<Operation> AdminOperations =
    [
        Operation.TeamView,
        Operation.TeamUpdate,
        Operation.TeamArchive,
        Operation.TeamUnarchive,
        Operation.TeamMemberAdd,
        Operation.TeamMemberRemove,
        Operation.TeamLeaderAssign,
        Operation.TeamLeaderRevoke,
        Operation.TaskView,
        Operation.WorklogViewDetail,
        Operation.ReportTeam,
    ];

    // 閲覧の操作（アーカイブしたチームでも行える）
    private static readonly HashSet<Operation> ViewOperations =
    [
        Operation.TeamView,
        Operation.TaskView,
        Operation.WorklogViewDetail,
        Operation.ReportTeam,
    ];

    public static bool IsViewOperation(Operation operation) => ViewOperations.Contains(operation);

    /// <summary>チームに関わる操作を判定する。</summary>
    public static AccessOutcome Evaluate(Operation operation, AccessFacts f)
    {
        // 1. 利用者が有効でなければ拒否する
        if (!f.IsActiveUser)
        {
            return AccessOutcome.Forbidden;
        }

        if (operation == Operation.TeamCreate)
        {
            return f.IsAdmin || f.IsLeaderAnywhere ? AccessOutcome.Allow : AccessOutcome.Forbidden;
        }

        // 3. 所属していないチームは、管理者に許される操作でなければ 404。
        //    管理者はチームを閲覧できるため、変更の操作は 403 にする（詳細設計書 4.5 の注記）
        if (f.Role is null && !(f.IsAdmin && AdminOperations.Contains(operation)))
        {
            return f.IsAdmin ? AccessOutcome.Forbidden : AccessOutcome.NotFound;
        }

        // 4. アーカイブしたチームは、閲覧とアーカイブの解除だけ
        if (f.IsArchived && !ViewOperations.Contains(operation) && operation != Operation.TeamUnarchive)
        {
            return AccessOutcome.Archived;
        }

        // 5. 表の条件
        var isLeader = f.Role == TeamRole.Leader;
        var isMember = f.Role == TeamRole.Member;
        var inTeam = f.Role is not null;
        var own = f.IsCreator && f.IsAssignee;

        switch (operation)
        {
            case Operation.TeamView:
            case Operation.TaskView:
                return inTeam || f.IsAdmin ? AccessOutcome.Allow : AccessOutcome.NotFound;

            case Operation.WorklogViewDetail:
            case Operation.ReportTeam:
                return isLeader || f.IsAdmin ? AccessOutcome.Allow : AccessOutcome.Forbidden;

            case Operation.TeamUpdate:
            case Operation.TeamArchive:
            case Operation.TeamUnarchive:
            case Operation.TeamMemberAdd:
            case Operation.TeamMemberRemove:
            case Operation.TeamLeaderAssign:
            case Operation.TeamLeaderRevoke:
                if (isLeader)
                {
                    return AccessOutcome.Allow;
                }

                if (f.IsAdmin)
                {
                    // 管理者の権限による変更は、再認証と許可したネットワークを求める（FR-ADM-08、NF-ACC-07）
                    return f.AdminStepUp ? AccessOutcome.Allow : AccessOutcome.ReauthRequired;
                }

                return AccessOutcome.Forbidden;

            case Operation.TeamTagManage:
            case Operation.TeamSharedViewManage:
            case Operation.TaskAssign:
            case Operation.TaskRestore:
                return Allow(isLeader);

            case Operation.TaskCreate:
                return Allow(isLeader || isMember);

            case Operation.TaskPlanEdit:
                return Allow(isLeader || (isMember && own));

            case Operation.TaskChildAdd:
                // 対象は親タスク（C と S は親についての値）
                return Allow(isLeader || (isMember && own));

            case Operation.TaskMove:
            case Operation.TaskDependencyEdit:
                return Allow(isLeader || (isMember && own && f.OtherIsOwn));

            case Operation.TaskDelete:
                return Allow(isLeader || (isMember && f.CreatedWholeSubtree && !f.HasWorkLogs));

            case Operation.TaskActualEdit:
                return Allow(isLeader || (isMember && f.IsAssignee));

            case Operation.WorklogCreate:
                return Allow(inTeam && f.IsAssignee);

            case Operation.WorklogEdit:
            case Operation.WorklogDelete:
            case Operation.CommentEdit:
            case Operation.CommentDelete:
                return Allow(inTeam && f.IsRecordOwner);

            case Operation.CommentCreate:
                return Allow(inTeam);

            default:
                // 判定できない場合は拒否する（NF-ACC-02）
                return AccessOutcome.Forbidden;
        }
    }

    private static AccessOutcome Allow(bool condition) => condition ? AccessOutcome.Allow : AccessOutcome.Forbidden;
}
