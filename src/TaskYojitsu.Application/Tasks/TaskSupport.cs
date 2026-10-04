using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Application.Tasks;

/// <summary>変更履歴（task_histories）の書き込み。業務データの変更と同じ SaveChanges で保存する。</summary>
public sealed class TaskHistoryWriter(IAppDbContext db, BusinessClock clock)
{
    public void Add(TaskItem task, Guid actorId, TaskHistoryKind kind, string? field = null, string? oldValue = null, string? newValue = null) =>
        Add(task.Id, task.TeamId, actorId, kind, field, oldValue, newValue);

    public void Add(Guid taskId, Guid teamId, Guid actorId, TaskHistoryKind kind, string? field = null, string? oldValue = null, string? newValue = null)
    {
        db.TaskHistories.Add(new TaskHistory
        {
            TaskId = taskId,
            TeamId = teamId,
            ActorId = actorId,
            OccurredAt = clock.UtcNow,
            Kind = kind,
            Field = field,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
        });
    }

    private static string? Truncate(string? value) => value is { Length: > 4000 } ? value[..4000] : value;
}

/// <summary>画面内の通知を作る（FR-NTF-01）。自分の操作で自分に通知はしない。</summary>
public sealed class Notifier(IAppDbContext db, BusinessClock clock)
{
    public void Notify(Guid recipientId, NotificationKind kind, Guid actorId, Guid? teamId, Guid? taskId)
    {
        if (recipientId == actorId)
        {
            return;
        }

        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = recipientId,
            Kind = kind,
            TeamId = teamId,
            TaskId = taskId,
            ActorId = actorId,
            CreatedAt = clock.UtcNow,
        });
    }
}

/// <summary>タスクの入力の検査で共通に使う規則（詳細設計書 4.6）。</summary>
internal static class TaskRules
{
    public static void ValidatePlannedDates(Validation v, DateOnly? start, DateOnly? end)
    {
        if (start.HasValue != end.HasValue)
        {
            v.Add(start.HasValue ? "plannedEnd" : "plannedStart", Msg.TskPlannedPair);
            return;
        }

        if (start is { } s && end is { } e)
        {
            if (!InRange(s))
            {
                v.Add("plannedStart", Msg.CmnChoice);
            }

            if (!InRange(e))
            {
                v.Add("plannedEnd", Msg.CmnChoice);
            }

            if (e < s)
            {
                v.Add("plannedEnd", Msg.TskPlannedOrder);
            }
        }
    }

    public static void ValidatePlannedMinutes(Validation v, int? minutes)
    {
        if (minutes is { } m && (m < 0 || m > Domain.Rules.Limits.PlannedMinutesMax || !Domain.Rules.Limits.IsMinuteStep(m)))
        {
            v.Add("plannedMinutes", Msg.TskPlannedMinutes);
        }
    }

    public static void ValidateActualDates(Validation v, DateOnly? start, DateOnly? end, DateOnly today)
    {
        if (start is { } s && (s > today || s < Domain.Rules.Limits.MinDate))
        {
            v.Add("actualStart", Msg.TskActualDates);
        }

        if (end is { } e && (e > today || e < Domain.Rules.Limits.MinDate))
        {
            v.Add("actualEnd", Msg.TskActualDates);
        }

        if (start is { } s2 && end is { } e2 && e2 < s2)
        {
            v.Add("actualEnd", Msg.TskActualDates);
        }
    }

    public static void ValidateMilestone(Validation v, bool isMilestone, DateOnly? start, DateOnly? end, int? minutes)
    {
        if (!isMilestone)
        {
            return;
        }

        if (start != end)
        {
            v.Add("isMilestone", Msg.TskMilestone);
        }

        if (minutes is > 0)
        {
            v.Add("plannedMinutes", Msg.TskMilestone);
        }
    }

    public static void ValidateProgress(Validation v, int? progress)
    {
        if (progress is { } p && (p < 0 || p > 100 || p % Domain.Rules.Limits.ProgressStep != 0))
        {
            v.Add("progress", Msg.TskProgress);
        }
    }

    private static bool InRange(DateOnly date) => date >= Domain.Rules.Limits.MinDate && date <= Domain.Rules.Limits.MaxDate;
}
