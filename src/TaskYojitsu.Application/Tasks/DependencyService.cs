using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Application.Tasks;

public sealed record AddDependencyRequest(Guid PredecessorId);

public sealed record DependencyResult(IReadOnlyList<string> Warnings);

/// <summary>依存関係（FR-TSK-13。API-33、34）。循環は受け付けず、日程が逆転していれば警告する。</summary>
public sealed class DependencyService(
    IAppDbContext db,
    AccessPolicy access,
    TaskHistoryWriter history,
    IAuditWriter audit,
    IDbLocks locks,
    BusinessClock clock)
{
    public async Task<DependencyResult> AddAsync(Guid successorId, AddDependencyRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var predecessorId = request.PredecessorId;
        TaskItem? predecessor = null;
        var acc = await access.RequireTaskAsync(successorId, Operation.TaskDependencyEdit, ct, adjust: async (task, facts) =>
        {
            predecessor = await db.Tasks.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == predecessorId && t.TeamId == task.TeamId && t.DeletedAt == null, ct);
            return facts with { OtherIsOwn = predecessor is not null && predecessor.CreatedBy == userId && predecessor.AssigneeId == userId };
        }, track: false);

        if (predecessor is null || predecessorId == successorId)
        {
            throw ValidationException.For("predecessorId", Msg.CmnChoice);
        }

        var teamId = acc.Task.TeamId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await locks.LockTeamHierarchyAsync(teamId, ct);

        var edges = await db.TaskDependencies.AsNoTracking()
            .Where(d => d.TeamId == teamId)
            .Select(d => new { d.PredecessorId, d.SuccessorId })
            .ToListAsync(ct);
        if (edges.Any(e => e.PredecessorId == predecessorId && e.SuccessorId == successorId))
        {
            return new DependencyResult(Warnings(acc.Task, predecessor));
        }

        // 後続から先行へたどれるなら循環になる
        var next = edges.ToLookup(e => e.PredecessorId, e => e.SuccessorId);
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>([successorId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == predecessorId)
            {
                throw new RuleViolationException(Msg.TskDependencyCycle);
            }

            foreach (var n in next[current].Where(seen.Add))
            {
                queue.Enqueue(n);
            }
        }

        db.TaskDependencies.Add(new TaskDependency
        {
            TeamId = teamId,
            PredecessorId = predecessorId,
            SuccessorId = successorId,
            CreatedAt = clock.UtcNow,
            CreatedBy = userId,
        });
        history.Add(successorId, teamId, userId, TaskHistoryKind.DependencyAdded, "predecessor", null, predecessor.Title);
        audit.Add(new AuditEntry("task.dependency.added", TargetType: "task", TargetId: successorId.ToString(), TeamId: teamId,
            Detail: new { predecessorId }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DependencyResult(Warnings(acc.Task, predecessor));
    }

    public async Task RemoveAsync(Guid successorId, Guid predecessorId, CancellationToken ct)
    {
        var userId = access.UserId;
        var acc = await access.RequireTaskAsync(successorId, Operation.TaskDependencyEdit, ct, adjust: async (task, facts) =>
        {
            var other = await db.Tasks.AsNoTracking()
                .Where(t => t.Id == predecessorId && t.TeamId == task.TeamId)
                .Select(t => new { t.CreatedBy, t.AssigneeId })
                .SingleOrDefaultAsync(ct);
            return facts with { OtherIsOwn = other is not null && other.CreatedBy == userId && other.AssigneeId == userId };
        }, track: false);

        var edge = await db.TaskDependencies.SingleOrDefaultAsync(d => d.PredecessorId == predecessorId && d.SuccessorId == successorId, ct)
            ?? throw new NotFoundException();
        var title = await db.Tasks.AsNoTracking().Where(t => t.Id == predecessorId).Select(t => t.Title).SingleOrDefaultAsync(ct);
        db.TaskDependencies.Remove(edge);
        history.Add(successorId, acc.Task.TeamId, userId, TaskHistoryKind.DependencyRemoved, "predecessor", title, null);
        audit.Add(new AuditEntry("task.dependency.removed", TargetType: "task", TargetId: successorId.ToString(), TeamId: acc.Task.TeamId,
            Detail: new { predecessorId }));
        await db.SaveChangesAsync(ct);
    }

    private static List<string> Warnings(TaskItem successor, TaskItem predecessor) =>
        predecessor.PlannedEnd is { } end && successor.PlannedStart is { } start && end > start
            ? [Msg.TskDependencyOrder]
            : [];
}
