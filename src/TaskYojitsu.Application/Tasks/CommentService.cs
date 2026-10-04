using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Tasks;

public sealed record CommentDto(
    Guid Id,
    Guid TaskId,
    Guid AuthorId,
    string AuthorName,
    string? Body,
    DateTime CreatedAt,
    DateTime? EditedAt,
    bool Deleted,
    bool CanEdit);

public sealed record CommentRequest(string? Body);

/// <summary>コメント（FR-TSK-11。API-29〜32）。本文はプレーンテキストで、URL の自動リンクは画面で行う。</summary>
public sealed class CommentService(
    IAppDbContext db,
    AccessPolicy access,
    Notifier notifier,
    IAuditWriter audit,
    BusinessClock clock)
{
    public async Task<IReadOnlyList<CommentDto>> ListAsync(Guid taskId, CancellationToken ct)
    {
        var acc = await access.RequireTaskAsync(taskId, Operation.TaskView, ct, track: false);
        var userId = access.UserId;
        var canWrite = acc.Facts.Role is not null && !acc.Facts.IsArchived;
        var rows = await db.Comments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .Join(db.Users, c => c.AuthorId, u => u.Id, (c, u) => new { c, u.DisplayName })
            .ToListAsync(ct);

        // 削除したコメントは「削除されました」と出す（本文は返さない）
        return [.. rows.Select(x => new CommentDto(
            x.c.Id, x.c.TaskId, x.c.AuthorId, x.DisplayName,
            x.c.DeletedAt is null ? x.c.Body : null,
            x.c.CreatedAt, x.c.EditedAt, x.c.DeletedAt is not null,
            canWrite && x.c.DeletedAt is null && x.c.AuthorId == userId))];
    }

    public async Task<CommentDto> CreateAsync(Guid taskId, CommentRequest request, CancellationToken ct)
    {
        var acc = await access.RequireTaskAsync(taskId, Operation.CommentCreate, ct, track: false);
        var body = Validate(request);
        var userId = access.UserId;
        var comment = new Comment
        {
            Id = Guid.CreateVersion7(),
            TeamId = acc.Task.TeamId,
            TaskId = taskId,
            AuthorId = userId,
            Body = body,
            CreatedAt = clock.UtcNow,
        };
        db.Comments.Add(comment);
        if (acc.Task.AssigneeId is { } assignee)
        {
            notifier.Notify(assignee, NotificationKind.CommentAdded, userId, acc.Task.TeamId, taskId);
        }

        audit.Add(new AuditEntry("comment.created", TargetType: "comment", TargetId: comment.Id.ToString(), TeamId: comment.TeamId,
            Detail: new { taskId, length = TextRules.Length(body) }));
        await db.SaveChangesAsync(ct);
        var user = await access.GetUserAsync(ct);
        return new CommentDto(comment.Id, taskId, userId, user.DisplayName, comment.Body, comment.CreatedAt, null, false, true);
    }

    public async Task<CommentDto> UpdateAsync(Guid commentId, CommentRequest request, CancellationToken ct)
    {
        var comment = await LoadAsync(commentId, Operation.CommentEdit, ct);
        var body = Validate(request);
        comment.Body = body;
        comment.EditedAt = clock.UtcNow;
        audit.Add(new AuditEntry("comment.updated", TargetType: "comment", TargetId: commentId.ToString(), TeamId: comment.TeamId,
            Detail: new { comment.TaskId, length = TextRules.Length(body) }));
        await db.SaveChangesAsync(ct);
        var user = await access.GetUserAsync(ct);
        return new CommentDto(comment.Id, comment.TaskId, comment.AuthorId, user.DisplayName, comment.Body, comment.CreatedAt, comment.EditedAt, false, true);
    }

    public async Task DeleteAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await LoadAsync(commentId, Operation.CommentDelete, ct);
        comment.DeletedAt = clock.UtcNow;
        audit.Add(new AuditEntry("comment.deleted", TargetType: "comment", TargetId: commentId.ToString(), TeamId: comment.TeamId,
            Detail: new { comment.TaskId }));
        await db.SaveChangesAsync(ct);
    }

    private async Task<Comment> LoadAsync(Guid commentId, Operation operation, CancellationToken ct)
    {
        var comment = await db.Comments.SingleOrDefaultAsync(c => c.Id == commentId && c.DeletedAt == null, ct)
            ?? throw new NotFoundException();
        var userId = access.UserId;
        await access.RequireTaskAsync(comment.TaskId, operation, ct, adjust: (_, facts) =>
            Task.FromResult(facts with { IsRecordOwner = comment.AuthorId == userId }), track: false);
        return comment;
    }

    private static string Validate(CommentRequest request)
    {
        var v = new Validation();
        var body = v.Multiline("body", request.Body, Limits.CommentMax, required: true);
        v.ThrowIfAny();
        return body!;
    }
}
