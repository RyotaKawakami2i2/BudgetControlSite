using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.WorkLogs;

/// <summary>週の入力表のマス。locked はダイアログで記録した日や、複数の記録がある日（ここでは変えられない）。</summary>
public sealed record TimesheetCellDto(DateOnly Date, int Minutes, bool Locked);

public sealed record TimesheetRowDto(
    Guid TaskId,
    string Title,
    Guid TeamId,
    string TeamName,
    TaskItemStatus Status,
    bool Editable,
    IReadOnlyList<TimesheetCellDto> Cells);

public sealed record TimesheetTaskOption(Guid TaskId, string Title, string TeamName, TaskItemStatus Status);

/// <summary>週の入力表（API-40）。</summary>
public sealed record TimesheetDto(
    DateOnly WeekStart,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<TimesheetRowDto> Rows,
    IReadOnlyList<int> DayTotals,
    IReadOnlyList<TimesheetTaskOption> Addable);

public sealed record TimesheetCellInput(Guid TaskId, DateOnly Date, int Minutes);

public sealed record SaveTimesheetRequest(DateOnly WeekStart, IReadOnlyList<TimesheetCellInput>? Cells);

/// <summary>週の入力表（FR-ACT-07。詳細設計書 5.4.5、7.6）。</summary>
public sealed class TimesheetService(
    IAppDbContext db,
    AccessPolicy access,
    TaskHistoryWriter history,
    WorkLogService workLogs,
    IAuditWriter audit,
    IDbLocks locks,
    BusinessClock clock)
{
    public async Task<TimesheetDto> GetAsync(DateOnly weekStart, CancellationToken ct)
    {
        var userId = access.UserId;
        EnsureMonday(weekStart);
        var weekEnd = weekStart.AddDays(6);
        var days = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();

        var memberTeams = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.RemovedAt == null)
            .Join(db.Teams, m => m.TeamId, t => t.Id, (m, t) => new { t.Id, t.Name, t.ArchivedAt })
            .ToListAsync(ct);
        var activeTeamIds = memberTeams.Where(t => t.ArchivedAt == null).Select(t => t.Id).ToList();

        // 自分が担当の、子を持たない、完了・中止以外のタスク
        var assigned = await db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == userId && t.DeletedAt == null && activeTeamIds.Contains(t.TeamId) && !t.IsMilestone
                && !db.Tasks.Any(c => c.ParentId == t.Id && c.DeletedAt == null))
            .Select(t => new { t.Id, t.Title, t.TeamId, t.Status, t.UpdatedAt })
            .ToListAsync(ct);

        var logs = await db.WorkLogs.AsNoTracking()
            .Where(w => w.UserId == userId && w.WorkDate >= weekStart && w.WorkDate <= weekEnd)
            .ToListAsync(ct);
        var loggedTaskIds = logs.Select(l => l.TaskId).Distinct().ToList();
        var loggedTasks = await db.Tasks.AsNoTracking()
            .Where(t => loggedTaskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.TeamId, t.Status, t.UpdatedAt, t.DeletedAt })
            .ToListAsync(ct);
        var teamNames = await db.Teams.AsNoTracking()
            .Where(t => loggedTasks.Select(x => x.TeamId).Contains(t.Id) || activeTeamIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new { t.Name, t.ArchivedAt }, ct);

        var rowTasks = assigned
            .Where(t => t.Status is not (TaskItemStatus.Done or TaskItemStatus.Cancelled))
            .Select(t => (t.Id, t.Title, t.TeamId, t.Status, Deleted: false))
            .Concat(loggedTasks.Select(t => (t.Id, t.Title, t.TeamId, t.Status, Deleted: t.DeletedAt != null)))
            .DistinctBy(t => t.Id)
            .OrderBy(t => teamNames.TryGetValue(t.TeamId, out var n) ? n.Name : "")
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();
        var editableIds = assigned.Select(t => t.Id).ToHashSet();

        var rows = rowTasks.Select(t => new TimesheetRowDto(
            t.Id,
            t.Title,
            t.TeamId,
            teamNames.TryGetValue(t.TeamId, out var team) ? team.Name : "",
            t.Status,
            Editable: editableIds.Contains(t.Id) && !t.Deleted,
            Cells: [.. days.Select(d =>
            {
                var cellLogs = logs.Where(l => l.TaskId == t.Id && l.WorkDate == d).ToList();
                var locked = cellLogs.Any(l => l.Source == WorkLogSource.Dialog) || cellLogs.Count > 1;
                return new TimesheetCellDto(d, cellLogs.Sum(l => l.Minutes), locked);
            })])).ToList();

        var dayTotals = days.Select(d => logs.Where(l => l.WorkDate == d).Sum(l => l.Minutes)).ToList();
        var inRows = rowTasks.Select(r => r.Id).ToHashSet();
        var addable = assigned
            .Where(t => !inRows.Contains(t.Id))
            .OrderByDescending(t => t.UpdatedAt)
            .Take(50)
            .Select(t => new TimesheetTaskOption(t.Id, t.Title, teamNames.TryGetValue(t.TeamId, out var n) ? n.Name : "", t.Status))
            .ToList();

        return new TimesheetDto(weekStart, days, rows, dayTotals, addable);
    }

    public async Task<TimesheetDto> SaveAsync(SaveTimesheetRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        EnsureMonday(request.WeekStart);
        var weekEnd = request.WeekStart.AddDays(6);
        var cells = request.Cells ?? [];
        var today = clock.Today;
        var v = new Validation();

        // 入力の形の検査
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell.Date < request.WeekStart || cell.Date > weekEnd)
            {
                v.Add($"cells[{i}]", Msg.CmnChoice);
            }
            else if (cell.Minutes < 0 || cell.Minutes > Limits.WorkLogMinutesMax || !Limits.IsMinuteStep(cell.Minutes))
            {
                v.Add($"cells[{i}]", Msg.WlMinutes);
            }
            else if (cell.Minutes > 0 && cell.Date > today)
            {
                v.Add($"cells[{i}]", Msg.WlFuture);
            }
        }

        if (cells.GroupBy(c => (c.TaskId, c.Date)).Any(g => g.Count() > 1))
        {
            v.Add("cells", Msg.CmnChoice);
        }

        v.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var date in cells.Select(c => c.Date).Distinct().Order())
        {
            await locks.LockUserDayAsync(userId, date, ct);
        }

        var taskIds = cells.Select(c => c.TaskId).Distinct().ToList();
        var tasks = await db.Tasks.Where(t => taskIds.Contains(t.Id) && t.DeletedAt == null).ToDictionaryAsync(t => t.Id, ct);
        var withChildren = await db.Tasks.AsNoTracking()
            .Where(c => c.ParentId != null && taskIds.Contains(c.ParentId.Value) && c.DeletedAt == null)
            .Select(c => c.ParentId!.Value).Distinct().ToListAsync(ct);
        var logs = await db.WorkLogs
            .Where(w => w.UserId == userId && w.WorkDate >= request.WeekStart && w.WorkDate <= weekEnd)
            .ToListAsync(ct);

        var now = clock.UtcNow;
        var changedTasks = new HashSet<Guid>();
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var field = $"cells[{i}]";
            var existing = logs.Where(l => l.TaskId == cell.TaskId && l.WorkDate == cell.Date).ToList();
            var current = existing.Sum(l => l.Minutes);
            if (current == cell.Minutes)
            {
                continue;
            }

            if (existing.Any(l => l.Source == WorkLogSource.Dialog) || existing.Count > 1)
            {
                v.Add(field, Msg.WlTimesheetLocked);
                continue;
            }

            // 記録できるタスクか（担当で、子を持たず、マイルストーンでない。権限は AccessPolicy で確かめる）
            if (!tasks.TryGetValue(cell.TaskId, out var task))
            {
                v.Add(field, Msg.CmnChoice);
                continue;
            }

            var facts = (await access.LoadTeamFactsAsync(task.TeamId, track: false, ct))?.Facts;
            if (facts is null || facts.Role is null)
            {
                v.Add(field, Msg.CmnChoice);
                continue;
            }

            var taskFacts = facts with { IsAssignee = task.AssigneeId == userId, IsCreator = task.CreatedBy == userId, IsRecordOwner = true };
            var operation = existing.Count == 0 ? Operation.WorklogCreate : Operation.WorklogEdit;
            if (!AccessPolicy.Can(operation, taskFacts))
            {
                v.Add(field, facts.IsArchived ? Msg.CmnChoice : Msg.WlNotAssignee);
                continue;
            }

            if (existing.Count == 0 && (task.IsMilestone || withChildren.Contains(task.Id)))
            {
                v.Add(field, Msg.WlSummary);
                continue;
            }

            if (existing.Count == 1)
            {
                var log = existing[0];
                var before = $"{DisplayNames.Date(log.WorkDate)} {DisplayNames.Hours(log.Minutes)}";
                if (cell.Minutes == 0)
                {
                    db.WorkLogs.Remove(log);
                    logs.Remove(log);
                    history.Add(task, userId, TaskHistoryKind.WorklogDeleted, "workLog", before, null);
                    audit.Add(new AuditEntry("worklog.deleted", TargetType: "work_log", TargetId: log.Id.ToString(), TeamId: log.TeamId,
                        Detail: new { log.TaskId, log.WorkDate, log.Minutes, source = "timesheet" }));
                }
                else
                {
                    var oldMinutes = log.Minutes;
                    log.Minutes = cell.Minutes;
                    log.Version++;
                    log.UpdatedAt = now;
                    history.Add(task, userId, TaskHistoryKind.WorklogUpdated, "workLog", before,
                        $"{DisplayNames.Date(log.WorkDate)} {DisplayNames.Hours(log.Minutes)}");
                    audit.Add(new AuditEntry("worklog.updated", TargetType: "work_log", TargetId: log.Id.ToString(), TeamId: log.TeamId,
                        Detail: new { log.TaskId, log.WorkDate, from = oldMinutes, to = log.Minutes, source = "timesheet" }));
                }
            }
            else
            {
                var log = new WorkLog
                {
                    Id = Guid.CreateVersion7(),
                    TeamId = task.TeamId,
                    TaskId = task.Id,
                    UserId = userId,
                    WorkDate = cell.Date,
                    Minutes = cell.Minutes,
                    Source = WorkLogSource.Timesheet,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.WorkLogs.Add(log);
                logs.Add(log);
                history.Add(task, userId, TaskHistoryKind.WorklogAdded, "workLog", null,
                    $"{DisplayNames.Date(log.WorkDate)} {DisplayNames.Hours(log.Minutes)}");
                audit.Add(new AuditEntry("worklog.created", TargetType: "work_log", TargetId: log.Id.ToString(), TeamId: log.TeamId,
                    Detail: new { log.TaskId, log.WorkDate, log.Minutes, source = "timesheet" }));
                if (changedTasks.Add(task.Id))
                {
                    workLogs.ApplyFirstWorkLog(task, cell.Date, userId, progress: null);
                }
            }
        }

        // 1 日の合計が 24 時間を超えないこと（同じ日の、ほかのタスクの記録も含む）
        for (var i = 0; i < cells.Count; i++)
        {
            var date = cells[i].Date;
            if (logs.Where(l => l.WorkDate == date).Sum(l => l.Minutes) > Limits.DailyMinutesMax)
            {
                v.Add($"cells[{i}]", Msg.WlDailyLimit);
            }
        }

        // 1 つでも誤りがあれば何も保存しない
        v.ThrowIfAny();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return await GetAsync(request.WeekStart, ct);
    }

    private static void EnsureMonday(DateOnly weekStart)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw ValidationException.For("weekStart", Msg.CmnChoice);
        }
    }
}
