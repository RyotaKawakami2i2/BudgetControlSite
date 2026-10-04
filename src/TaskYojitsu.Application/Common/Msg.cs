namespace TaskYojitsu.Application.Common;

/// <summary>メッセージ ID（詳細設計書 7.7）。文言は画面側（web/src/lib/messages.ts）が持つ。</summary>
public static class Msg
{
    public const string CmnTooLong = "MSG-CMN-002";
    public const string CmnChoice = "MSG-CMN-003";
    public const string CmnRequired = "MSG-CMN-004";

    public const string TskTitle = "MSG-TSK-001";
    public const string TskPlannedOrder = "MSG-TSK-002";
    public const string TskPlannedPair = "MSG-TSK-003";
    public const string TskPlannedMinutes = "MSG-TSK-004";
    public const string TskParentSelf = "MSG-TSK-005";
    public const string TskDepth = "MSG-TSK-006";
    public const string TskAssignee = "MSG-TSK-007";
    public const string TskSummaryReadonly = "MSG-TSK-008";
    public const string TskActualDates = "MSG-TSK-009";
    public const string TskDoneNeedsEnd = "MSG-TSK-010";
    public const string TskMilestone = "MSG-TSK-011";
    public const string TskProgress = "MSG-TSK-012";
    public const string TskHasWorkLogs = "MSG-TSK-013";
    public const string TskParentDeleted = "MSG-TSK-014";
    public const string TskStatusTransition = "MSG-TSK-015";
    public const string TskDependencyCycle = "MSG-TSK-016";
    public const string TskDependencyOrder = "MSG-TSK-017";

    public const string WlMinutes = "MSG-WL-001";
    public const string WlDailyLimit = "MSG-WL-002";
    public const string WlFuture = "MSG-WL-003";
    public const string WlSummary = "MSG-WL-004";
    public const string WlNotAssignee = "MSG-WL-005";
    public const string WlTimesheetLocked = "MSG-WL-006";

    public const string TemName = "MSG-TEM-001";
    public const string TemNameDuplicate = "MSG-TEM-002";
    public const string TemLastLeader = "MSG-TEM-003";
    public const string TemTagDuplicate = "MSG-TEM-004";

    public const string AdmLastAdmin = "MSG-ADM-001";
    public const string AdmEmailDuplicate = "MSG-ADM-002";
}
