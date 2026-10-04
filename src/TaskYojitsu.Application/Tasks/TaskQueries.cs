using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Tasks;

/// <summary>タスクの詳細と変更履歴（API-21、API-28）。</summary>
public sealed class TaskQueries(IAppDbContext db, AccessPolicy access, TaskDataLoader loader)
{
    public async Task<TaskDetailDto> GetDetailAsync(Guid taskId, CancellationToken ct)
    {
        var userId = access.UserId;
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskView, ct, track: false);
        var task = acc.Task;
        var set = await loader.LoadTeamsAsync([task.TeamId], ct);
        var row = set.ById[taskId];
        var rollup = set.Rollups[taskId];
        var teamFacts = acc.Team.Facts;

        var tagRows = await db.TaskTags.AsNoTracking()
            .Where(t => t.TaskId == taskId)
            .Join(db.Tags, tt => tt.TagId, t => t.Id, (tt, t) => new TagDto(t.Id, t.TeamId, t.Name, t.Color))
            .ToListAsync(ct);

        var predIds = await db.TaskDependencies.AsNoTracking().Where(d => d.SuccessorId == taskId).Select(d => d.PredecessorId).ToListAsync(ct);
        var succIds = await db.TaskDependencies.AsNoTracking().Where(d => d.PredecessorId == taskId).Select(d => d.SuccessorId).ToListAsync(ct);
        DependencyDto ToDep(Guid id)
        {
            var r = set.Rollups[id];
            return new DependencyDto(id, set.ById[id].Title, r.PlannedStart, r.PlannedEnd, r.Status);
        }

        var predecessors = predIds.Where(set.ById.ContainsKey).Select(ToDep).ToList();
        var successors = succIds.Where(set.ById.ContainsKey).Select(ToDep).ToList();

        var path = set.Ancestors(taskId).Reverse().Select(t => new TaskRef(t.Id, t.Title)).ToList();
        var userIds = new[] { task.AssigneeId, (Guid?)task.CreatedBy }.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var can = TaskDataLoader.ComputeCan(set, row, teamFacts, userId);
        var facts = acc.Facts;
        var isLeader = teamFacts.Role == TeamRole.Leader;
        var nextStatuses = rollup.IsSummary || !can.EditActual
            ? []
            : StatusTransitions.NextStatuses(task.Status, new TransitionActor(isLeader, facts.IsAssignee, facts.IsCreator));

        var detailCan = new TaskDetailCan(
            can.EditPlan,
            can.Assign,
            can.EditActual,
            can.LogWork,
            can.AddChild,
            can.Delete,
            Move: AccessPolicy.Can(Operation.TaskMove, facts with { OtherIsOwn = true }),
            EditDependencies: AccessPolicy.Can(Operation.TaskDependencyEdit, facts with { OtherIsOwn = true }),
            Comment: AccessPolicy.Can(Operation.CommentCreate, facts),
            ViewAllWorkLogs: AccessPolicy.Can(Operation.WorklogViewDetail, facts),
            NextStatuses: nextStatuses);

        var warnings = new List<string>();
        if (rollup.PlannedStart is { } start && predecessors.Any(p => p.PlannedEnd is { } end && end > start))
        {
            warnings.Add(Msg.TskDependencyOrder);
        }

        var dto = TaskDataLoader.ToDto(
            set, row, [.. tagRows.Select(t => t.Id)], predecessors.Select(p => p.Id).ToList(), can);
        return new TaskDetailDto(
            dto,
            task.Description,
            task.ResultNote,
            acc.Team.Team.Name,
            acc.Team.Team.IsArchived,
            teamFacts.Role?.ToCode() ?? "admin_view",
            path,
            task.AssigneeId is { } a ? names.GetValueOrDefault(a) : null,
            names.GetValueOrDefault(task.CreatedBy, ""),
            tagRows,
            predecessors,
            successors,
            set.Children.TryGetValue(taskId, out var kids) ? kids.Count : 0,
            task.CreatedAt,
            task.UpdatedAt,
            detailCan,
            warnings);
    }

    /// <summary>
    /// チームのタスクの一覧（API-35 の簡易版）。親タスクや先行タスクを選ぶときの選択肢に使う。階層の順に並べる。
    /// </summary>
    public async Task<IReadOnlyList<TaskOptionDto>> ListOptionsAsync(Guid teamId, CancellationToken ct)
    {
        await access.RequireTeamAsync(teamId, Operation.TaskView, ct);
        var rows = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == teamId && t.DeletedAt == null)
            .Select(t => new { t.Id, t.ParentId, t.Depth, t.SortOrder, t.Title, t.IsMilestone, t.Status, t.AssigneeId, t.CreatedBy })
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        var children = rows.Where(r => r.ParentId is { } p && byId.ContainsKey(p)).ToLookup(r => r.ParentId!.Value);
        var result = new List<TaskOptionDto>(rows.Count);
        void Visit(Guid? parent)
        {
            var kids = parent is { } id ? children[id] : rows.Where(r => r.ParentId is null || !byId.ContainsKey(r.ParentId.Value));
            foreach (var r in kids.OrderBy(r => r.SortOrder).ThenBy(r => r.Id))
            {
                result.Add(new TaskOptionDto(r.Id, r.ParentId, r.Depth, r.Title, r.IsMilestone, r.Status, children[r.Id].Any(),
                    r.AssigneeId, r.CreatedBy));
                Visit(r.Id);
            }
        }

        Visit(null);
        return result;
    }

    public async Task<CursorPage<TaskHistoryDto>> GetHistoryAsync(Guid taskId, string? cursor, int? limit, CancellationToken ct)
    {
        await access.RequireTaskAsync(taskId, Operation.TaskView, ct, track: false);
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var query = db.TaskHistories.AsNoTracking().Where(h => h.TaskId == taskId);
        if (long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var before))
        {
            query = query.Where(h => h.Id < before);
        }

        var rows = await query
            .OrderByDescending(h => h.Id)
            .Take(take + 1)
            .Join(db.Users, h => h.ActorId, u => u.Id, (h, u) => new TaskHistoryDto(
                h.Id, h.OccurredAt, h.ActorId, u.DisplayName, h.Kind, h.Field, h.OldValue, h.NewValue))
            .ToListAsync(ct);
        var items = rows.OrderByDescending(r => r.Id).Take(take).ToList();
        var next = rows.Count > take ? items[^1].Id.ToString(CultureInfo.InvariantCulture) : null;
        return new CursorPage<TaskHistoryDto>(items, next);
    }
}
