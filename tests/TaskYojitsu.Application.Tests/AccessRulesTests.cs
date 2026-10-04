using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using static TaskYojitsu.Application.Security.AccessOutcome;

namespace TaskYojitsu.Application.Tests;

/// <summary>
/// 権限の判定表（詳細設計書 4.5）。表を変えたら、このテストと結合テストの表（permission-matrix.csv）も直す。
/// </summary>
public class AccessRulesTests
{
    // 利用者の種類ごとの判定の材料（タスクについての C・S などは、各テストで加える）
    private static readonly AccessFacts Leader = new() { Role = TeamRole.Leader, IsLeaderAnywhere = true };
    private static readonly AccessFacts Member = new() { Role = TeamRole.Member };
    private static readonly AccessFacts Outsider = new() { Role = null };
    private static readonly AccessFacts Admin = new() { IsAdmin = true, AdminStepUp = true };
    private static readonly AccessFacts AdminWithoutReauth = new() { IsAdmin = true, AdminStepUp = false };

    /// <summary>
    /// 操作ごとの期待する結果。順に、リーダー、メンバー（作成者でも担当者でもない）、チーム外、
    /// 管理者（チーム外、再認証あり）、管理者（チーム外、再認証なし）。
    /// </summary>
    private static readonly (Operation Operation, AccessOutcome[] Expected)[] Table =
    [
        (Operation.TeamView, [Allow, Allow, NotFound, Allow, Allow]),
        (Operation.TeamCreate, [Allow, Forbidden, Forbidden, Allow, Allow]),
        (Operation.TeamUpdate, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamArchive, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamUnarchive, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamMemberAdd, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamMemberRemove, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamLeaderAssign, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamLeaderRevoke, [Allow, Forbidden, NotFound, Allow, ReauthRequired]),
        (Operation.TeamTagManage, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TeamSharedViewManage, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskView, [Allow, Allow, NotFound, Allow, Allow]),
        (Operation.TaskCreate, [Allow, Allow, NotFound, Forbidden, Forbidden]),
        (Operation.TaskPlanEdit, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskAssign, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskChildAdd, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskMove, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskDependencyEdit, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskDelete, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskRestore, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.TaskActualEdit, [Allow, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.WorklogCreate, [Forbidden, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.WorklogEdit, [Forbidden, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.WorklogDelete, [Forbidden, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.WorklogViewDetail, [Allow, Forbidden, NotFound, Allow, Allow]),
        (Operation.CommentCreate, [Allow, Allow, NotFound, Forbidden, Forbidden]),
        (Operation.CommentEdit, [Forbidden, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.CommentDelete, [Forbidden, Forbidden, NotFound, Forbidden, Forbidden]),
        (Operation.ReportTeam, [Allow, Forbidden, NotFound, Allow, Allow]),
    ];

    public static TheoryData<Operation> Operations() => [.. Table.Select(row => row.Operation)];

    [Theory]
    [MemberData(nameof(Operations))]
    public void 利用者の種類ごとに表のとおりに判定する(Operation operation)
    {
        var expected = Table.Single(row => row.Operation == operation).Expected;
        AccessFacts[] actors = [Leader, Member, Outsider, Admin, AdminWithoutReauth];

        var actual = actors.Select(facts => AccessRules.Evaluate(operation, facts)).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 表にすべての操作がある()
    {
        Assert.Equal(Enum.GetValues<Operation>().Order(), Table.Select(row => row.Operation).Order());
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void 有効でない利用者は常に拒否する(Operation operation)
    {
        var facts = Leader with { IsActiveUser = false, IsAdmin = true, AdminStepUp = true };

        Assert.Equal(Forbidden, AccessRules.Evaluate(operation, facts));
    }

    [Fact]
    public void TaskCreate_チーム外のリーダーでも所属していないチームには作れない()
    {
        var leaderOfAnotherTeam = Outsider with { IsLeaderAnywhere = true };

        Assert.Equal(NotFound, AccessRules.Evaluate(Operation.TaskCreate, leaderOfAnotherTeam));
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.TeamCreate, leaderOfAnotherTeam));
    }

    [Theory]
    [InlineData(true, true, Allow)]
    [InlineData(true, false, Forbidden)]
    [InlineData(false, true, Forbidden)]
    public void TaskPlanEdit_メンバーは自分が作成して担当しているタスクだけ(bool creator, bool assignee, AccessOutcome expected)
    {
        var facts = Member with { IsCreator = creator, IsAssignee = assignee };

        Assert.Equal(expected, AccessRules.Evaluate(Operation.TaskPlanEdit, facts));
        Assert.Equal(expected, AccessRules.Evaluate(Operation.TaskChildAdd, facts));
    }

    [Theory]
    [InlineData(true, Allow)]
    [InlineData(false, Forbidden)]
    public void TaskMoveとDependency_メンバーは相手のタスクも自分のものであること(bool otherIsOwn, AccessOutcome expected)
    {
        var facts = Member with { IsCreator = true, IsAssignee = true, OtherIsOwn = otherIsOwn };

        Assert.Equal(expected, AccessRules.Evaluate(Operation.TaskMove, facts));
        Assert.Equal(expected, AccessRules.Evaluate(Operation.TaskDependencyEdit, facts));
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.TaskMove, Leader with { OtherIsOwn = false }));
    }

    [Theory]
    [InlineData(true, false, Allow)]
    [InlineData(true, true, Forbidden)]
    [InlineData(false, false, Forbidden)]
    public void TaskDelete_メンバーは子孫まで自分が作成し作業実績がないこと(bool wholeSubtree, bool hasWorkLogs, AccessOutcome expected)
    {
        var facts = Member with { IsCreator = true, IsAssignee = true, CreatedWholeSubtree = wholeSubtree, HasWorkLogs = hasWorkLogs };

        Assert.Equal(expected, AccessRules.Evaluate(Operation.TaskDelete, facts));
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.TaskDelete, Leader with { HasWorkLogs = hasWorkLogs }));
    }

    [Fact]
    public void TaskActualEdit_メンバーは担当者なら作成者でなくても変えられる()
    {
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.TaskActualEdit, Member with { IsAssignee = true }));
    }

    [Fact]
    public void WorklogCreate_リーダーでも担当者でなければ記録できない()
    {
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.WorklogCreate, Leader with { IsAssignee = true }));
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.WorklogCreate, Member with { IsAssignee = true }));
        Assert.Equal(Forbidden, AccessRules.Evaluate(Operation.WorklogCreate, Admin with { IsAssignee = true }));
    }

    [Theory]
    [InlineData(Operation.WorklogEdit)]
    [InlineData(Operation.WorklogDelete)]
    [InlineData(Operation.CommentEdit)]
    [InlineData(Operation.CommentDelete)]
    public void 記録の変更は本人だけで_チームから外れたら対象が見えない(Operation operation)
    {
        Assert.Equal(Allow, AccessRules.Evaluate(operation, Member with { IsRecordOwner = true }));
        Assert.Equal(Allow, AccessRules.Evaluate(operation, Leader with { IsRecordOwner = true }));
        Assert.Equal(NotFound, AccessRules.Evaluate(operation, Outsider with { IsRecordOwner = true }));
        Assert.Equal(Forbidden, AccessRules.Evaluate(operation, Admin with { IsRecordOwner = true }));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void アーカイブしたチームは閲覧とアーカイブの解除だけ(Operation operation)
    {
        var facts = Leader with { IsArchived = true, IsCreator = true, IsAssignee = true, IsRecordOwner = true, CreatedWholeSubtree = true };
        var allowed = operation is Operation.TeamView or Operation.TaskView or Operation.WorklogViewDetail
            or Operation.ReportTeam or Operation.TeamUnarchive or Operation.TeamCreate;

        Assert.Equal(allowed ? Allow : Archived, AccessRules.Evaluate(operation, facts));
    }

    [Fact]
    public void アーカイブしたチームでも_所属していなければ404を先に返す()
    {
        Assert.Equal(NotFound, AccessRules.Evaluate(Operation.TaskPlanEdit, Outsider with { IsArchived = true }));
        Assert.Equal(Allow, AccessRules.Evaluate(Operation.TeamUnarchive, Admin with { IsArchived = true }));
    }

    [Fact]
    public void 閲覧の操作の一覧()
    {
        Assert.True(AccessRules.IsViewOperation(Operation.TaskView));
        Assert.False(AccessRules.IsViewOperation(Operation.TaskPlanEdit));
    }
}
