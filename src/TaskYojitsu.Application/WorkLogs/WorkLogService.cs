using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.WorkLogs;

public sealed record WorkLogDto(
    Guid Id,
    Guid TaskId,
    string TaskTitle,
    Guid UserId,
    string UserName,
    DateOnly WorkDate,
    int Minutes,
    string? Note,
    WorkLogSource Source,
    int Version,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool CanEdit);

public sealed record WorkLogTotalDto(Guid UserId, string UserName, int Minutes);

/// <summary>作業実績の一覧（API-36）。メンバーには自分の明細と人ごとの合計、リーダーと管理者には全員の明細。</summary>
public sealed record WorkLogListDto(
    IReadOnlyList<WorkLogDto> Mine,
    IReadOnlyList<WorkLogTotalDto> Totals,
    IReadOnlyList<WorkLogDto>? All,
    int TotalMinutes);

public sealed record CreateWorkLogRequest(DateOnly? WorkDate, int? Minutes, string? Note, int? Progress, int? TaskVersion);

public sealed record UpdateWorkLogRequest(int Version, DateOnly? WorkDate, int? Minutes, string? Note);

/// <summary>作業実績を記録した後のタスクの要点。</summary>
public sealed record TaskStateDto(Guid Id, TaskItemStatus Status, int Progress, DateOnly? ActualStart, int ActualMinutes, int Version);

public sealed record WorkLogResultDto(WorkLogDto WorkLog, TaskStateDto Task);

/// <summary>作業実績（FR-ACT-01〜05。API-36〜39）。</summary>
public sealed class WorkLogService(
    IAppDbContext db,
    AccessPolicy access,
    TaskHistoryWriter history,
    IAuditWriter audit,
    IDbLocks locks,
    BusinessClock clock)
{
    public async Task<WorkLogListDto> ListAsync(Guid taskId, CancellationToken ct)
    {
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskView, ct, track: false);
        var userId = access.UserId;
        var canWrite = acc.Facts.Role is not null && !acc.Facts.IsArchived;

        // まとめタスクは、子孫の作業実績も含める
        var teamTasks = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == acc.Task.TeamId && t.DeletedAt == null)
            .Select(t => new { t.Id, t.ParentId, t.Title })
            .ToListAsync(ct);
        var children = teamTasks.Where(t => t.ParentId is not null).ToLookup(t => t.ParentId!.Value);
        var titles = teamTasks.ToDictionary(t => t.Id, t => t.Title);
        var ids = new List<Guid>();
        var stack = new Stack<Guid>([taskId]);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            ids.Add(id);
            foreach (var c in children[id])
            {
                stack.Push(c.Id);
            }
        }

        var logs = await db.WorkLogs.AsNoTracking()
            .Where(w => ids.Contains(w.TaskId))
            .Join(db.Users, w => w.UserId, u => u.Id, (w, u) => new { w, u.DisplayName })
            .OrderByDescending(x => x.w.WorkDate).ThenByDescending(x => x.w.CreatedAt)
            .ToListAsync(ct);

        WorkLogDto ToDto(WorkLog w, string name) => new(
            w.Id, w.TaskId, titles.GetValueOrDefault(w.TaskId, ""), w.UserId, name, w.WorkDate, w.Minutes, w.Note, w.Source,
            w.Version, w.CreatedAt, w.UpdatedAt, canWrite && w.UserId == userId);

        var mine = logs.Where(x => x.w.UserId == userId).Select(x => ToDto(x.w, x.DisplayName)).ToList();
        var totals = logs.GroupBy(x => new { x.w.UserId, x.DisplayName })
            .Select(g => new WorkLogTotalDto(g.Key.UserId, g.Key.DisplayName, g.Sum(x => x.w.Minutes)))
            .OrderByDescending(t => t.Minutes)
            .ToList();
        var all = AccessPolicy.Can(Operation.WorklogViewDetail, acc.Facts)
            ? logs.Select(x => ToDto(x.w, x.DisplayName)).ToList()
            : null;
        return new WorkLogListDto(mine, totals, all, logs.Sum(x => x.w.Minutes));
    }

    public async Task<WorkLogResultDto> CreateAsync(Guid taskId, CreateWorkLogRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var acc = await access.RequireTaskAsync(taskId, Operation.WorklogCreate, ct);
        var task = acc.Task;
        var today = clock.Today;

        var v = new Validation();
        ValidateDate(v, request.WorkDate, today);
        ValidateMinutes(v, request.Minutes);
        var note = v.SingleLine("note", request.Note, Limits.WorkLogNoteMax, required: false);
        if (request.Progress is not null)
        {
            TaskRules.ValidateProgress(v, request.Progress);
        }

        v.ThrowIfAny();

        if (task.IsMilestone || await db.Tasks.AnyAsync(c => c.ParentId == taskId && c.DeletedAt == null, ct))
        {
            // まとめタスクとマイルストーンには、作業実績を記録できない
            throw new RuleViolationException(Msg.WlSummary);
        }

        if (request.TaskVersion is { } taskVersion && taskVersion != task.Version)
        {
            throw new ConflictException();
        }

        var workDate = request.WorkDate!.Value;
        var minutes = request.Minutes!.Value;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockUserDayAsync(userId, workDate, ct);
        await EnsureDailyLimitAsync(userId, workDate, minutes, excludeId: null, ct);

        var now = clock.UtcNow;
        var log = new WorkLog
        {
            Id = Guid.CreateVersion7(),
            TeamId = task.TeamId,
            TaskId = taskId,
            UserId = userId,
            WorkDate = workDate,
            Minutes = minutes,
            Note = note,
            Source = WorkLogSource.Dialog,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.WorkLogs.Add(log);
        history.Add(task, userId, TaskHistoryKind.WorklogAdded, "workLog", null, $"{DisplayNames.Date(workDate)} {DisplayNames.Hours(minutes)}");
        ApplyFirstWorkLog(task, workDate, userId, request.Progress);
        audit.Add(new AuditEntry("worklog.created", TargetType: "work_log", TargetId: log.Id.ToString(), TeamId: task.TeamId,
            Detail: new { taskId, workDate, minutes }));
        await SaveAsync(ct);
        await tx.CommitAsync(ct);

        var user = await access.GetUserAsync(ct);
        return new WorkLogResultDto(
            new WorkLogDto(log.Id, taskId, task.Title, userId, user.DisplayName, log.WorkDate, log.Minutes, log.Note, log.Source,
                log.Version, log.CreatedAt, log.UpdatedAt, true),
            await TaskStateAsync(task, ct));
    }

    public async Task<WorkLogResultDto> UpdateAsync(Guid workLogId, UpdateWorkLogRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var log = await db.WorkLogs.SingleOrDefaultAsync(w => w.Id == workLogId, ct) ?? throw new NotFoundException();
        var acc = await access.RequireTaskAsync(log.TaskId, Operation.WorklogEdit, ct,
            adjust: (_, facts) => Task.FromResult(facts with { IsRecordOwner = log.UserId == userId }));
        if (request.Version != log.Version)
        {
            throw new ConflictException();
        }

        var today = clock.Today;
        var v = new Validation();
        ValidateDate(v, request.WorkDate, today);
        ValidateMinutes(v, request.Minutes);
        var note = v.SingleLine("note", request.Note, Limits.WorkLogNoteMax, required: false);
        v.ThrowIfAny();

        var newDate = request.WorkDate!.Value;
        var newMinutes = request.Minutes!.Value;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var date in new[] { log.WorkDate, newDate }.Distinct().Order())
        {
            await locks.LockUserDayAsync(userId, date, ct);
        }

        await EnsureDailyLimitAsync(userId, newDate, newMinutes, excludeId: log.Id, ct);
        var before = $"{DisplayNames.Date(log.WorkDate)} {DisplayNames.Hours(log.Minutes)}";
        var oldValues = new { log.WorkDate, log.Minutes };
        log.WorkDate = newDate;
        log.Minutes = newMinutes;
        log.Note = note;
        log.Version++;
        log.UpdatedAt = clock.UtcNow;
        history.Add(acc.Task, userId, TaskHistoryKind.WorklogUpdated, "workLog", before, $"{DisplayNames.Date(newDate)} {DisplayNames.Hours(newMinutes)}");
        audit.Add(new AuditEntry("worklog.updated", TargetType: "work_log", TargetId: log.Id.ToString(), TeamId: log.TeamId,
            Detail: new { log.TaskId, from = oldValues, to = new { log.WorkDate, log.Minutes } }));
        await SaveAsync(ct);
        await tx.CommitAsync(ct);

        var user = await access.GetUserAsync(ct);
        return new WorkLogResultDto(
            new WorkLogDto(log.Id, log.TaskId, acc.Task.Title, userId, user.DisplayName, log.WorkDate, log.Minutes, log.Note, log.Source,
                log.Version, log.CreatedAt, log.UpdatedAt, true),
            await TaskStateAsync(acc.Task, ct));
    }

    public async Task DeleteAsync(Guid workLogId, CancellationToken ct)
    {
        var userId = access.UserId;
        var log = await db.WorkLogs.SingleOrDefaultAsync(w => w.Id == workLogId, ct) ?? throw new NotFoundException();
        var acc = await access.RequireTaskAsync(log.TaskId, Operation.WorklogDelete, ct,
            adjust: (_, facts) => Task.FromResult(facts with { IsRecordOwner = log.UserId == userId }));
        db.WorkLogs.Remove(log);
        history.Add(acc.Task, userId, TaskHistoryKind.WorklogDeleted, "workLog", $"{DisplayNames.Date(log.WorkDate)} {DisplayNames.Hours(log.Minutes)}", null);
        audit.Add(new AuditEntry("worklog.deleted", TargetType: "work_log", TargetId: log.Id.ToString(), TeamId: log.TeamId,
            Detail: new { log.TaskId, log.WorkDate, log.Minutes }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 作業実績を記録したときの自動の値（FR-ACT-03）。実績開始日が空なら作業日を入れ（作業日の方が前なら前に寄せる）、
    /// 未着手なら進行中にする。進捗率を指定した場合は、同じトランザクションで変える。
    /// </summary>
    internal void ApplyFirstWorkLog(TaskItem task, DateOnly workDate, Guid userId, int? progress)
    {
        var changed = false;
        if (task.ActualStart is null || workDate < task.ActualStart)
        {
            // 実績開始日は実際に作業を始めた日。より前の作業日が記録されたら、その日に合わせる
            history.Add(task, userId, TaskHistoryKind.Updated, "actualStart", DisplayNames.Date(task.ActualStart), DisplayNames.Date(workDate));
            task.ActualStart = workDate;
            changed = true;
        }

        if (progress is { } p && p != task.Progress && task.Status != TaskItemStatus.Done)
        {
            history.Add(task, userId, TaskHistoryKind.Updated, "progress", $"{task.Progress}%", $"{p}%");
            task.Progress = (short)p;
            changed = true;
        }

        if (task.Status == TaskItemStatus.NotStarted)
        {
            history.Add(task, userId, TaskHistoryKind.Updated, "status", DisplayNames.Of(TaskItemStatus.NotStarted), DisplayNames.Of(TaskItemStatus.InProgress));
            task.Status = TaskItemStatus.InProgress;
            changed = true;
        }

        if (changed)
        {
            task.Version++;
            task.UpdatedAt = clock.UtcNow;
            task.UpdatedBy = userId;
        }
    }

    internal static void ValidateDate(Validation v, DateOnly? date, DateOnly today)
    {
        if (date is not { } d)
        {
            v.Add("workDate", Msg.CmnRequired);
        }
        else if (d > today)
        {
            v.Add("workDate", Msg.WlFuture);
        }
        else if (d < Limits.MinDate)
        {
            v.Add("workDate", Msg.CmnChoice);
        }
    }

    internal static void ValidateMinutes(Validation v, int? minutes)
    {
        if (minutes is not { } m || m < Limits.WorkLogMinutesMin || m > Limits.WorkLogMinutesMax || !Limits.IsMinuteStep(m))
        {
            v.Add("minutes", Msg.WlMinutes);
        }
    }

    /// <summary>同じ人・同じ日の合計が 24 時間を超えないことを確かめる（ロックを取った後に呼ぶ）。</summary>
    private async Task EnsureDailyLimitAsync(Guid userId, DateOnly date, int minutes, Guid? excludeId, CancellationToken ct)
    {
        var sum = await db.WorkLogs
            .Where(w => w.UserId == userId && w.WorkDate == date && (excludeId == null || w.Id != excludeId))
            .SumAsync(w => (int?)w.Minutes, ct) ?? 0;
        if (sum + minutes > Limits.DailyMinutesMax)
        {
            throw ValidationException.For("minutes", Msg.WlDailyLimit);
        }
    }

    private async Task<TaskStateDto> TaskStateAsync(TaskItem task, CancellationToken ct)
    {
        var minutes = await db.WorkLogs.Where(w => w.TaskId == task.Id).SumAsync(w => (int?)w.Minutes, ct) ?? 0;
        return new TaskStateDto(task.Id, task.Status, task.Progress, task.ActualStart, minutes, task.Version);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
    }
}
