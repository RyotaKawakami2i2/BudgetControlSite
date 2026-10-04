using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Tasks;

/// <summary>タスクの登録・変更・移動・削除・復元（FR-TSK-01〜10）。</summary>
public sealed class TaskService(
    IAppDbContext db,
    AccessPolicy access,
    TaskDataLoader loader,
    TaskHistoryWriter history,
    Notifier notifier,
    IAuditWriter audit,
    IDbLocks locks,
    BusinessClock clock,
    IOptions<BusinessOptions> options)
{
    // ---------------------------------------------------------------- 登録

    public async Task<GanttTaskDto> CreateAsync(CreateTaskRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var v = new Validation();
        var title = v.SingleLine("title", request.Title, Limits.TaskTitleMax, required: true, Msg.TskTitle, Msg.TskTitle);
        var description = v.Multiline("description", request.Description, Limits.TaskTextMax);
        var priority = Priority.Medium;
        if (request.Priority is not null && v.TryCode<Priority>("priority", request.Priority, out var p))
        {
            priority = p.Value;
        }

        TaskRules.ValidatePlannedDates(v, request.PlannedStart, request.PlannedEnd);
        TaskRules.ValidatePlannedMinutes(v, request.PlannedMinutes);
        TaskRules.ValidateMilestone(v, request.IsMilestone, request.PlannedStart, request.PlannedEnd, request.PlannedMinutes);
        var tagIds = (request.TagIds ?? []).Distinct().ToList();
        if (tagIds.Count > Limits.TagsPerTaskMax)
        {
            v.Add("tagIds", Msg.CmnChoice);
        }

        v.ThrowIfAny();

        // 権限: 親を指定したら task.child.add、しなければ task.create
        TeamAccess team;
        TaskItem? parent = null;
        if (request.ParentId is { } parentId)
        {
            var parentAccess = await access.RequireTaskAsync(parentId, Operation.TaskChildAdd, ct, track: false);
            if (parentAccess.Task.TeamId != request.TeamId)
            {
                throw ValidationException.For("parentId", Msg.CmnChoice);
            }

            parent = parentAccess.Task;
            team = parentAccess.Team;
        }
        else
        {
            team = await access.RequireTeamAsync(request.TeamId, Operation.TaskCreate, ct);
        }

        var isLeader = team.Role == TeamRole.Leader;
        var assigneeId = request.AssigneeId;
        if (!isLeader)
        {
            // メンバーが登録する場合、担当者は自分（省略したら自分）
            if (assigneeId is not null && assigneeId != userId)
            {
                throw new ForbiddenException();
            }

            assigneeId = userId;
        }
        else if (assigneeId is { } a && !await IsCurrentMemberAsync(team.Team.Id, a, ct))
        {
            throw ValidationException.For("assigneeId", Msg.TskAssignee);
        }

        await EnsureTagsAsync(team.Team.Id, tagIds, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        short depth = 1;
        if (parent is not null)
        {
            await locks.LockTeamHierarchyAsync(team.Team.Id, ct);
            var current = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == parent.Id && t.DeletedAt == null, ct)
                ?? throw new NotFoundException();
            if (current.IsMilestone)
            {
                throw ValidationException.For("parentId", Msg.TskMilestone);
            }

            if (!HierarchyRules.CanPlace(current.Depth, 1))
            {
                throw ValidationException.For("parentId", Msg.TskDepth);
            }

            depth = (short)(current.Depth + 1);
        }

        var maxSort = await db.Tasks
            .Where(t => t.TeamId == team.Team.Id && t.ParentId == request.ParentId && t.DeletedAt == null)
            .MaxAsync(t => (int?)t.SortOrder, ct) ?? 0;

        var now = clock.UtcNow;
        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            TeamId = team.Team.Id,
            ParentId = request.ParentId,
            Depth = depth,
            SortOrder = maxSort + HierarchyRules.SortStep,
            Title = title!,
            Description = description,
            AssigneeId = assigneeId,
            CreatedBy = userId,
            Status = TaskItemStatus.NotStarted,
            Priority = priority,
            PlannedStart = request.PlannedStart,
            PlannedEnd = request.PlannedEnd,
            PlannedMinutes = request.IsMilestone ? null : request.PlannedMinutes,
            Progress = 0,
            IsMilestone = request.IsMilestone,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = userId,
        };
        db.Tasks.Add(task);
        foreach (var tagId in tagIds)
        {
            db.TaskTags.Add(new TaskTag { TeamId = task.TeamId, TaskId = task.Id, TagId = tagId });
        }

        history.Add(task, userId, TaskHistoryKind.Created, newValue: task.Title);
        audit.Add(new AuditEntry("task.created", TargetType: "task", TargetId: task.Id.ToString(), TeamId: task.TeamId,
            Detail: new { parentId = task.ParentId, assigneeId = task.AssigneeId, task.PlannedStart, task.PlannedEnd, task.PlannedMinutes, task.IsMilestone }));
        if (assigneeId is { } assignee)
        {
            notifier.Notify(assignee, NotificationKind.TaskAssigned, userId, task.TeamId, task.Id);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await LoadDtoAsync(task.Id, team.Facts, ct);
    }

    // ---------------------------------------------------------------- 変更

    public async Task<GanttTaskDto> UpdateAsync(Guid taskId, UpdateTaskRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskView, ct);
        var task = acc.Task;
        var facts = acc.Facts;

        var planTouched = request.Title.HasValue || request.Description.HasValue || request.PlannedStart.HasValue
            || request.PlannedEnd.HasValue || request.PlannedMinutes.HasValue || request.Priority.HasValue
            || request.TagIds.HasValue || request.IsMilestone.HasValue;
        var assignTouched = request.AssigneeId.HasValue && request.AssigneeId.Value != task.AssigneeId;
        var actualTouched = request.Status.HasValue || request.Progress.HasValue || request.ActualStart.HasValue
            || request.ActualEnd.HasValue || request.ResultNote.HasValue;

        // 項目ごとに権限を確かめ、1 つでも権限のない項目があれば全体を受け付けない
        if (planTouched)
        {
            await access.EnsureAsync(Operation.TaskPlanEdit, facts, "task", taskId.ToString(), task.TeamId, ct);
        }

        if (assignTouched)
        {
            await access.EnsureAsync(Operation.TaskAssign, facts, "task", taskId.ToString(), task.TeamId, ct);
        }

        if (actualTouched)
        {
            await access.EnsureAsync(Operation.TaskActualEdit, facts, "task", taskId.ToString(), task.TeamId, ct);
        }

        if (!planTouched && !assignTouched && !actualTouched)
        {
            // 変更がなければ、閲覧以外の権限は要らない
            await access.EnsureAsync(Operation.TaskActualEdit, facts, "task", taskId.ToString(), task.TeamId, ct);
        }

        if (request.Version != task.Version)
        {
            throw new ConflictException(await LoadDtoAsync(taskId, acc.Team.Facts, ct));
        }

        var isSummary = await db.Tasks.AnyAsync(c => c.ParentId == taskId && c.DeletedAt == null, ct);
        var today = clock.Today;
        var v = new Validation();

        if (isSummary)
        {
            // まとめタスクの日程・工数・進捗・状態は子から計算するため、変えられない
            foreach (var (present, field) in new[]
                     {
                         (request.PlannedStart.HasValue, "plannedStart"), (request.PlannedEnd.HasValue, "plannedEnd"),
                         (request.PlannedMinutes.HasValue, "plannedMinutes"), (request.Progress.HasValue, "progress"),
                         (request.Status.HasValue, "status"), (request.ActualStart.HasValue, "actualStart"),
                         (request.ActualEnd.HasValue, "actualEnd"),
                     })
            {
                if (present)
                {
                    v.Add(field, Msg.TskSummaryReadonly);
                }
            }

            if (request.IsMilestone is { HasValue: true, Value: true })
            {
                v.Add("isMilestone", Msg.TskMilestone);
            }
        }

        // 新しい値を組み立てる
        var title = request.Title.HasValue
            ? v.SingleLine("title", request.Title.Value, Limits.TaskTitleMax, required: true, Msg.TskTitle, Msg.TskTitle)
            : task.Title;
        var description = request.Description.HasValue
            ? v.Multiline("description", request.Description.Value, Limits.TaskTextMax)
            : task.Description;
        var resultNote = request.ResultNote.HasValue
            ? v.Multiline("resultNote", request.ResultNote.Value, Limits.TaskTextMax)
            : task.ResultNote;
        var priority = task.Priority;
        if (request.Priority.HasValue && v.TryCode<Priority>("priority", request.Priority.Value, out var pr))
        {
            priority = pr.Value;
        }

        var plannedStart = request.PlannedStart.HasValue ? request.PlannedStart.Value : task.PlannedStart;
        var plannedEnd = request.PlannedEnd.HasValue ? request.PlannedEnd.Value : task.PlannedEnd;
        var plannedMinutes = request.PlannedMinutes.HasValue ? request.PlannedMinutes.Value : task.PlannedMinutes;
        var isMilestone = request.IsMilestone.HasValue ? request.IsMilestone.Value ?? false : task.IsMilestone;
        if (isMilestone && !task.IsMilestone && request.IsMilestone.HasValue && !request.PlannedMinutes.HasValue)
        {
            // マイルストーンにしたら工数は持たない
            plannedMinutes = null;
        }

        if (!isSummary)
        {
            TaskRules.ValidatePlannedDates(v, plannedStart, plannedEnd);
            TaskRules.ValidatePlannedMinutes(v, plannedMinutes);
            TaskRules.ValidateMilestone(v, isMilestone, plannedStart, plannedEnd, plannedMinutes);
        }

        var assigneeId = request.AssigneeId.HasValue ? request.AssigneeId.Value : task.AssigneeId;
        if (assignTouched && assigneeId is { } newAssignee && !await IsCurrentMemberAsync(task.TeamId, newAssignee, ct))
        {
            v.Add("assigneeId", Msg.TskAssignee);
        }

        // 状態と実績
        var status = task.Status;
        if (request.Status.HasValue && v.TryCode<TaskItemStatus>("status", request.Status.Value, out var st))
        {
            status = st.Value;
        }

        var progress = request.Progress.HasValue ? request.Progress.Value : task.Progress;
        if (request.Progress.HasValue && request.Progress.Value is null)
        {
            v.Add("progress", Msg.TskProgress);
        }

        TaskRules.ValidateProgress(v, progress);
        var actualStart = request.ActualStart.HasValue ? request.ActualStart.Value : task.ActualStart;
        var actualEnd = request.ActualEnd.HasValue ? request.ActualEnd.Value : task.ActualEnd;

        if (!isSummary && !v.HasErrors)
        {
            // 進捗率を 0 より大きくしたら、未着手から進行中に自動で変わる
            if (!request.Status.HasValue && task.Status == TaskItemStatus.NotStarted && progress > 0)
            {
                status = TaskItemStatus.InProgress;
            }

            if (status != task.Status)
            {
                if (!StatusTransitions.IsDefined(task.Status, status))
                {
                    v.Add("status", Msg.TskStatusTransition);
                }
                else if (!StatusTransitions.CanPerform(task.Status, status,
                             new TransitionActor(facts.Role == TeamRole.Leader, facts.IsAssignee, facts.IsCreator)))
                {
                    throw new ForbiddenException();
                }
            }

            // 未着手から進行中になったら、実績開始日が空なら今日を入れる
            if (status == TaskItemStatus.InProgress && task.Status == TaskItemStatus.NotStarted && actualStart is null)
            {
                actualStart = today;
            }

            if (status == TaskItemStatus.Done)
            {
                if (task.Status != TaskItemStatus.Done)
                {
                    // 完了にするときは、実績終了日を同じ要求に含める。進捗率は 100 にする
                    if (actualEnd is null || !request.ActualEnd.HasValue)
                    {
                        v.Add("actualEnd", Msg.TskDoneNeedsEnd);
                    }

                    progress = 100;
                    actualStart ??= actualEnd;
                }
                else if (progress != 100)
                {
                    v.Add("progress", Msg.TskProgress);
                }
            }
            else
            {
                // 完了以外の状態は実績終了日を持たない（完了から再開したら空に戻す）
                actualEnd = null;
            }

            TaskRules.ValidateActualDates(v, actualStart, actualEnd, today);
        }

        List<Guid>? newTagIds = null;
        if (request.TagIds.HasValue)
        {
            newTagIds = (request.TagIds.Value ?? []).Distinct().ToList();
            if (newTagIds.Count > Limits.TagsPerTaskMax)
            {
                v.Add("tagIds", Msg.CmnChoice);
            }
        }

        v.ThrowIfAny();
        if (newTagIds is not null)
        {
            await EnsureTagsAsync(task.TeamId, newTagIds, ct);
        }

        // 変更を当てて、履歴と監査ログに残す
        var oldAssigneeId = task.AssigneeId;
        var names = await LoadUserNamesAsync([task.AssigneeId, assigneeId], ct);
        var changes = new Dictionary<string, object?>();

        void Change<T>(string field, T oldValue, T newValue, Action apply, Func<T, string?> display, bool sensitiveText = false)
        {
            if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
            {
                return;
            }

            apply();
            history.Add(task, userId, TaskHistoryKind.Updated, field, display(oldValue), display(newValue));
            changes[field] = sensitiveText ? new { changed = true } : new { from = display(oldValue), to = display(newValue) };
        }

        Change("title", task.Title, title!, () => task.Title = title!, x => x);
        Change("description", task.Description, description, () => task.Description = description, x => x, sensitiveText: true);
        Change("priority", task.Priority, priority, () => task.Priority = priority, DisplayNames.Of);
        Change("plannedStart", task.PlannedStart, plannedStart, () => task.PlannedStart = plannedStart, DisplayNames.Date);
        Change("plannedEnd", task.PlannedEnd, plannedEnd, () => task.PlannedEnd = plannedEnd, DisplayNames.Date);
        Change("plannedMinutes", task.PlannedMinutes, plannedMinutes, () => task.PlannedMinutes = plannedMinutes, DisplayNames.Hours);
        Change("isMilestone", task.IsMilestone, isMilestone, () => task.IsMilestone = isMilestone, x => x ? "はい" : "いいえ");
        Change("assigneeId", task.AssigneeId, assigneeId, () => task.AssigneeId = assigneeId,
            x => x is { } id ? names.GetValueOrDefault(id, "") : "未割り当て");
        Change("status", task.Status, status, () => task.Status = status, DisplayNames.Of);
        Change("progress", task.Progress, (short)(progress ?? 0), () => task.Progress = (short)(progress ?? 0), x => $"{x}%");
        Change("actualStart", task.ActualStart, actualStart, () => task.ActualStart = actualStart, DisplayNames.Date);
        Change("actualEnd", task.ActualEnd, actualEnd, () => task.ActualEnd = actualEnd, DisplayNames.Date);
        Change("resultNote", task.ResultNote, resultNote, () => task.ResultNote = resultNote, x => x, sensitiveText: true);

        if (newTagIds is not null)
        {
            var current = await db.TaskTags.Where(t => t.TaskId == taskId).ToListAsync(ct);
            var currentIds = current.Select(t => t.TagId).ToHashSet();
            if (!currentIds.SetEquals(newTagIds))
            {
                var tagNames = await db.Tags.AsNoTracking().Where(t => t.TeamId == task.TeamId)
                    .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
                string Show(IEnumerable<Guid> ids) => string.Join("、", ids.Select(id => tagNames.GetValueOrDefault(id, "")).Order());
                db.TaskTags.RemoveRange(current.Where(t => !newTagIds.Contains(t.TagId)));
                foreach (var tagId in newTagIds.Where(id => !currentIds.Contains(id)))
                {
                    db.TaskTags.Add(new TaskTag { TeamId = task.TeamId, TaskId = taskId, TagId = tagId });
                }

                history.Add(task, userId, TaskHistoryKind.Updated, "tagIds", Show(currentIds), Show(newTagIds));
                changes["tagIds"] = new { from = currentIds, to = newTagIds };
            }
        }

        if (changes.Count == 0)
        {
            return await LoadDtoAsync(taskId, acc.Team.Facts, ct);
        }

        if (oldAssigneeId != assigneeId)
        {
            // 新しい担当者には割り当て、前の担当者には割り当ての解除を知らせる
            if (assigneeId is { } newOne)
            {
                notifier.Notify(newOne, NotificationKind.TaskAssigned, userId, task.TeamId, task.Id);
            }

            if (oldAssigneeId is { } oldOne)
            {
                notifier.Notify(oldOne, NotificationKind.TaskUnassigned, userId, task.TeamId, task.Id);
            }
        }

        task.Version++;
        task.UpdatedAt = clock.UtcNow;
        task.UpdatedBy = userId;
        audit.Add(new AuditEntry("task.updated", TargetType: "task", TargetId: taskId.ToString(), TeamId: task.TeamId,
            Detail: new { changes }));

        await SaveWithConcurrencyAsync(taskId, acc.Team.Facts, ct);
        return await LoadDtoAsync(taskId, acc.Team.Facts, ct);
    }

    // ---------------------------------------------------------------- 移動

    public async Task<GanttTaskDto> MoveAsync(Guid taskId, MoveTaskRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskMove, ct, adjust: async (task, facts) =>
        {
            if (request.ParentId is not { } newParentId)
            {
                return facts with { OtherIsOwn = true };
            }

            var parent = await db.Tasks.AsNoTracking()
                .Where(t => t.Id == newParentId && t.TeamId == task.TeamId && t.DeletedAt == null)
                .Select(t => new { t.CreatedBy, t.AssigneeId })
                .SingleOrDefaultAsync(ct);
            return facts with { OtherIsOwn = parent is not null && parent.CreatedBy == userId && parent.AssigneeId == userId };
        });
        var task = acc.Task;
        if (request.Version != task.Version)
        {
            throw new ConflictException(await LoadDtoAsync(taskId, acc.Team.Facts, ct));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockTeamHierarchyAsync(task.TeamId, ct);

        // ロックを取った後の状態で検査する
        var rows = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == task.TeamId && t.DeletedAt == null)
            .Select(t => new { t.Id, t.ParentId, t.Depth, t.SortOrder, t.IsMilestone, t.Title })
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        var children = rows.Where(r => r.ParentId is not null).ToLookup(r => r.ParentId!.Value);

        var oldParentTitle = task.ParentId is { } op && byId.TryGetValue(op, out var oldParent) ? oldParent.Title : "（最上位）";
        int parentDepth = 0;
        var newParentTitle = "（最上位）";
        if (request.ParentId is { } parentId)
        {
            if (!byId.TryGetValue(parentId, out var parent))
            {
                throw ValidationException.For("parentId", Msg.CmnChoice);
            }

            if (parent.IsMilestone)
            {
                throw ValidationException.For("parentId", Msg.TskMilestone);
            }

            // 自分自身と子孫は選べない（移動先の親から祖先をたどって確かめる）
            var cursor = parent;
            var guard = 0;
            while (cursor is not null && guard++ < 64)
            {
                if (cursor.Id == taskId)
                {
                    throw ValidationException.For("parentId", Msg.TskParentSelf);
                }

                cursor = cursor.ParentId is { } next && byId.TryGetValue(next, out var up) ? up : null;
            }

            parentDepth = parent.Depth;
            newParentTitle = parent.Title;
        }

        int Height(Guid id)
        {
            var kids = children[id].ToList();
            return kids.Count == 0 ? 1 : 1 + kids.Max(k => Height(k.Id));
        }

        if (!HierarchyRules.CanPlace(parentDepth, Height(taskId)))
        {
            throw ValidationException.For("parentId", Msg.TskDepth);
        }

        // 並び順: afterTaskId の直後（null なら先頭）
        var siblings = rows
            .Where(r => r.ParentId == request.ParentId && r.Id != taskId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToList();
        int index;
        if (request.AfterTaskId is { } afterId)
        {
            index = siblings.FindIndex(s => s.Id == afterId);
            if (index < 0)
            {
                throw ValidationException.For("afterTaskId", Msg.CmnChoice);
            }

            index++;
        }
        else
        {
            index = 0;
        }

        int? before = index > 0 ? siblings[index - 1].SortOrder : null;
        int? after = index < siblings.Count ? siblings[index].SortOrder : null;
        var sortOrder = HierarchyRules.Between(before, after);
        if (sortOrder is null)
        {
            // 間が詰まったら、同じ親の子を 1,024 刻みで振り直す
            var ordered = siblings.Select(s => s.Id).ToList();
            ordered.Insert(index, taskId);
            var tracked = await db.Tasks.Where(t => ordered.Contains(t.Id)).ToListAsync(ct);
            for (var i = 0; i < ordered.Count; i++)
            {
                var entity = tracked.Single(t => t.Id == ordered[i]);
                entity.SortOrder = (i + 1) * HierarchyRules.SortStep;
            }

            sortOrder = (index + 1) * HierarchyRules.SortStep;
        }

        var oldParentId = task.ParentId;
        var depthDelta = parentDepth + 1 - task.Depth;
        task.ParentId = request.ParentId;
        task.SortOrder = sortOrder.Value;
        task.Depth = (short)(parentDepth + 1);
        if (depthDelta != 0)
        {
            var descendantIds = new List<Guid>();
            var stack = new Stack<Guid>(children[taskId].Select(c => c.Id));
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                descendantIds.Add(id);
                foreach (var c in children[id])
                {
                    stack.Push(c.Id);
                }
            }

            var descendants = await db.Tasks.Where(t => descendantIds.Contains(t.Id)).ToListAsync(ct);
            foreach (var d in descendants)
            {
                d.Depth = (short)(d.Depth + depthDelta);
            }
        }

        task.Version++;
        task.UpdatedAt = clock.UtcNow;
        task.UpdatedBy = userId;
        if (oldParentId != request.ParentId)
        {
            history.Add(task, userId, TaskHistoryKind.Moved, "parentId", oldParentTitle, newParentTitle);
        }

        audit.Add(new AuditEntry("task.moved", TargetType: "task", TargetId: taskId.ToString(), TeamId: task.TeamId,
            Detail: new { fromParentId = oldParentId, toParentId = request.ParentId, request.AfterTaskId }));
        await SaveWithConcurrencyAsync(taskId, acc.Team.Facts, ct);
        await tx.CommitAsync(ct);
        return await LoadDtoAsync(taskId, acc.Team.Facts, ct);
    }

    // ---------------------------------------------------------------- 削除と復元

    public async Task DeleteAsync(Guid taskId, int version, CancellationToken ct)
    {
        var userId = access.UserId;
        var subtreeIds = new List<Guid>();
        var hasWorkLogs = false;
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskDelete, ct, adjust: async (task, facts) =>
        {
            var rows = await db.Tasks.AsNoTracking()
                .Where(t => t.TeamId == task.TeamId && t.DeletedAt == null)
                .Select(t => new { t.Id, t.ParentId, t.CreatedBy })
                .ToListAsync(ct);
            var children = rows.Where(r => r.ParentId is not null).ToLookup(r => r.ParentId!.Value);
            var stack = new Stack<Guid>([task.Id]);
            var createdAll = true;
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                subtreeIds.Add(id);
                createdAll &= rows.First(r => r.Id == id).CreatedBy == userId;
                foreach (var c in children[id])
                {
                    stack.Push(c.Id);
                }
            }

            hasWorkLogs = await db.WorkLogs.AnyAsync(w => subtreeIds.Contains(w.TaskId), ct);
            if (facts.Role == TeamRole.Member && createdAll && hasWorkLogs)
            {
                // 作業実績があるタスクは、メンバーは削除できない（リーダーに依頼する）
                throw new RuleViolationException(Msg.TskHasWorkLogs);
            }

            return facts with { CreatedWholeSubtree = createdAll, HasWorkLogs = hasWorkLogs };
        });

        var root = acc.Task;
        if (version != root.Version)
        {
            throw new ConflictException(await LoadDtoAsync(taskId, acc.Team.Facts, ct));
        }

        var now = clock.UtcNow;
        var batchId = Guid.CreateVersion7();
        var targets = await db.Tasks.Where(t => subtreeIds.Contains(t.Id) && t.DeletedAt == null).ToListAsync(ct);
        foreach (var t in targets)
        {
            t.DeletedAt = now;
            t.DeletedBy = userId;
            t.DeleteBatchId = batchId;
            t.UpdatedAt = now;
            t.UpdatedBy = userId;
            if (t.Id == taskId)
            {
                t.Version++;
            }
        }

        history.Add(root, userId, TaskHistoryKind.Deleted, newValue: targets.Count > 1 ? $"子タスク {targets.Count - 1} 件を含む" : null);
        audit.Add(new AuditEntry("task.deleted", TargetType: "task", TargetId: taskId.ToString(), TeamId: root.TeamId,
            Detail: new { count = targets.Count, batchId, hasWorkLogs }));
        await SaveWithConcurrencyAsync(taskId, acc.Team.Facts, ct);
    }

    public async Task<IReadOnlyList<DeletedTaskDto>> ListDeletedAsync(Guid teamId, CancellationToken ct)
    {
        await access.RequireTeamAsync(teamId, Operation.TaskRestore, ct);
        var since = clock.UtcNow.AddDays(-options.Value.RestoreWindowDays);
        var deleted = await db.Tasks.AsNoTracking()
            .Where(t => t.TeamId == teamId && t.DeletedAt != null && t.DeletedAt >= since)
            .Select(t => new { t.Id, t.Title, t.ParentId, DeletedAt = t.DeletedAt!.Value, t.DeletedBy, t.DeleteBatchId })
            .ToListAsync(ct);

        var ids = deleted.Select(d => d.Id).ToHashSet();
        var byId = deleted.ToDictionary(d => d.Id);
        var userIds = deleted.Where(d => d.DeletedBy is not null).Select(d => d.DeletedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var parentIds = deleted.Where(d => d.ParentId is not null).Select(d => d.ParentId!.Value).Distinct().ToList();
        var deletedParents = await db.Tasks.AsNoTracking()
            .Where(t => parentIds.Contains(t.Id) && t.DeletedAt != null)
            .Select(t => new { t.Id, t.DeleteBatchId })
            .ToDictionaryAsync(t => t.Id, t => t.DeleteBatchId, ct);

        // 同時に削除した部分木の根だけを出す
        return [.. deleted
            .Where(d => d.ParentId is null || !ids.Contains(d.ParentId.Value) || byId[d.ParentId.Value].DeleteBatchId != d.DeleteBatchId)
            .OrderByDescending(d => d.DeletedAt)
            .Select(d => new DeletedTaskDto(
                d.Id, d.Title, d.ParentId, d.DeletedAt,
                d.DeletedBy is { } by ? names.GetValueOrDefault(by) : null,
                deleted.Count(x => x.DeleteBatchId == d.DeleteBatchId) - 1,
                CanRestore: d.ParentId is not { } pid || !deletedParents.TryGetValue(pid, out var batch) || batch == d.DeleteBatchId))];
    }

    public async Task<GanttTaskDto> RestoreAsync(Guid taskId, CancellationToken ct)
    {
        var userId = access.UserId;
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskRestore, ct, includeDeleted: true);
        var task = acc.Task;
        if (task.DeletedAt is null)
        {
            return await LoadDtoAsync(taskId, acc.Team.Facts, ct);
        }

        if (task.DeletedAt < clock.UtcNow.AddDays(-options.Value.RestoreWindowDays))
        {
            throw new NotFoundException();
        }

        if (task.ParentId is { } parentId)
        {
            var parent = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == parentId, ct);
            if (parent is null || (parent.DeletedAt is not null && parent.DeleteBatchId != task.DeleteBatchId))
            {
                throw new RuleViolationException(Msg.TskParentDeleted);
            }
        }

        var batchId = task.DeleteBatchId;
        var targets = await db.Tasks.Where(t => t.DeleteBatchId == batchId && t.DeletedAt != null).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var t in targets)
        {
            t.DeletedAt = null;
            t.DeletedBy = null;
            t.DeleteBatchId = null;
            t.UpdatedAt = now;
            t.UpdatedBy = userId;
        }

        task.Version++;
        history.Add(task, userId, TaskHistoryKind.Restored);
        audit.Add(new AuditEntry("task.restored", TargetType: "task", TargetId: taskId.ToString(), TeamId: task.TeamId,
            Detail: new { count = targets.Count }));
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(taskId, acc.Team.Facts, ct);
    }

    // ---------------------------------------------------------------- 共通

    /// <summary>1 件のタスクを、ガントの行の形で読み直す。</summary>
    public async Task<GanttTaskDto> LoadDtoAsync(Guid taskId, AccessFacts teamFacts, CancellationToken ct)
    {
        var teamId = await db.Tasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => t.TeamId).SingleAsync(ct);
        var set = await loader.LoadTeamsAsync([teamId], ct);
        if (!set.ById.TryGetValue(taskId, out var row))
        {
            throw new NotFoundException();
        }

        var tags = await db.TaskTags.AsNoTracking().Where(t => t.TaskId == taskId).Select(t => t.TagId).ToListAsync(ct);
        var preds = await db.TaskDependencies.AsNoTracking().Where(d => d.SuccessorId == taskId).Select(d => d.PredecessorId).ToListAsync(ct);
        var can = TaskDataLoader.ComputeCan(set, row, teamFacts, access.UserId);
        return TaskDataLoader.ToDto(set, row, tags, preds, can);
    }

    private async Task SaveWithConcurrencyAsync(Guid taskId, AccessFacts teamFacts, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ConflictException(await LoadDtoAsync(taskId, teamFacts, ct));
        }
    }

    private Task<bool> IsCurrentMemberAsync(Guid teamId, Guid userId, CancellationToken ct) =>
        db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId && m.RemovedAt == null, ct);

    private async Task EnsureTagsAsync(Guid teamId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct)
    {
        if (tagIds.Count == 0)
        {
            return;
        }

        var ids = tagIds.ToList();
        var count = await db.Tags.CountAsync(t => t.TeamId == teamId && ids.Contains(t.Id), ct);
        if (count != ids.Count)
        {
            throw ValidationException.For("tagIds", Msg.CmnChoice);
        }
    }

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}
