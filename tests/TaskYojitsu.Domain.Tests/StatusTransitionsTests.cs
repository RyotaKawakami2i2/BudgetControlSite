using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;
using static TaskYojitsu.Domain.Codes.TaskItemStatus;

namespace TaskYojitsu.Domain.Tests;

/// <summary>タスクの状態の変化（詳細設計書 4.1 の表）。</summary>
public class StatusTransitionsTests
{
    private static readonly TransitionActor Leader = new(IsLeader: true, IsAssignee: false, IsCreator: false);
    private static readonly TransitionActor Owner = new(IsLeader: false, IsAssignee: true, IsCreator: true);
    private static readonly TransitionActor Assignee = new(IsLeader: false, IsAssignee: true, IsCreator: false);
    private static readonly TransitionActor Creator = new(IsLeader: false, IsAssignee: false, IsCreator: true);
    private static readonly TransitionActor Other = new(IsLeader: false, IsAssignee: false, IsCreator: false);

    /// <summary>設計書の表。値は（リーダー、作成者かつ担当者、担当者だけ、作成者だけ、そのほか）が行えるか。</summary>
    private static readonly (TaskItemStatus From, TaskItemStatus To, bool[] Expected)[] Rows =
    [
        (NotStarted, InProgress, [true, true, true, false, false]),
        (NotStarted, Done, [true, true, true, false, false]),
        (InProgress, Done, [true, true, true, false, false]),
        (Done, InProgress, [true, true, true, false, false]),
        (NotStarted, OnHold, [true, true, false, false, false]),
        (InProgress, OnHold, [true, true, false, false, false]),
        (OnHold, InProgress, [true, true, false, false, false]),
        (OnHold, NotStarted, [true, true, false, false, false]),
        (NotStarted, Cancelled, [true, true, false, false, false]),
        (InProgress, Cancelled, [true, true, false, false, false]),
        (OnHold, Cancelled, [true, true, false, false, false]),
        (Cancelled, NotStarted, [true, false, false, false, false]),
    ];

    public static TheoryData<TaskItemStatus, TaskItemStatus, bool[]> Table()
    {
        var data = new TheoryData<TaskItemStatus, TaskItemStatus, bool[]>();
        foreach (var (from, to, expected) in Rows)
        {
            data.Add(from, to, expected);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void CanPerform_表のとおりに許可する(TaskItemStatus from, TaskItemStatus to, bool[] expected)
    {
        TransitionActor[] actors = [Leader, Owner, Assignee, Creator, Other];

        Assert.True(StatusTransitions.IsDefined(from, to));
        Assert.Equal(expected, actors.Select(a => StatusTransitions.CanPerform(from, to, a)).ToArray());
    }

    [Fact]
    public void 表にない変化は誰も行えない()
    {
        var defined = Rows.Select(row => (row.From, row.To)).ToHashSet();
        foreach (var from in Enum.GetValues<TaskItemStatus>())
        {
            foreach (var to in Enum.GetValues<TaskItemStatus>())
            {
                if (from == to || defined.Contains((from, to)))
                {
                    continue;
                }

                Assert.False(StatusTransitions.IsDefined(from, to), $"{from} → {to}");
                Assert.False(StatusTransitions.CanPerform(from, to, Leader), $"{from} → {to}");
            }
        }
    }

    [Fact]
    public void 同じ状態のままは常に可()
    {
        Assert.True(StatusTransitions.CanPerform(Done, Done, Other));
    }

    [Fact]
    public void NextStatuses_立場に応じた選択肢を返す()
    {
        Assert.Equal([Done], StatusTransitions.NextStatuses(InProgress, Assignee));
        Assert.Equal([OnHold, Done, Cancelled], StatusTransitions.NextStatuses(InProgress, Owner));
        Assert.Equal([NotStarted], StatusTransitions.NextStatuses(Cancelled, Leader));
        Assert.Empty(StatusTransitions.NextStatuses(Cancelled, Owner));
    }
}
