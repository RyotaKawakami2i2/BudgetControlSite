using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Teams;

/// <summary>チームのタグ（FR-TEM-03。API-16〜18）。</summary>
public sealed class TagService(IAppDbContext db, AccessPolicy access, IAuditWriter audit, BusinessClock clock)
{
    public async Task<TagDto> CreateAsync(Guid teamId, TagRequest request, CancellationToken ct)
    {
        await access.RequireTeamAsync(teamId, Operation.TeamTagManage, ct);
        var (name, color) = Validate(request);
        await EnsureUniqueAsync(teamId, name, null, ct);
        var now = clock.UtcNow;
        var tag = new Tag
        {
            Id = Guid.CreateVersion7(),
            TeamId = teamId,
            Name = name,
            Color = color,
            CreatedAt = now,
            CreatedBy = access.UserId,
            UpdatedAt = now,
            UpdatedBy = access.UserId,
        };
        db.Tags.Add(tag);
        audit.Add(new AuditEntry("tag.created", TargetType: "tag", TargetId: tag.Id.ToString(), TeamId: teamId,
            Detail: new { name, color = color.ToCode() }));
        await db.SaveChangesAsync(ct);
        return new TagDto(tag.Id, teamId, tag.Name, tag.Color);
    }

    public async Task<TagDto> UpdateAsync(Guid tagId, TagRequest request, CancellationToken ct)
    {
        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == tagId, ct) ?? throw new NotFoundException();
        await access.RequireTeamAsync(tag.TeamId, Operation.TeamTagManage, ct);
        var (name, color) = Validate(request);
        await EnsureUniqueAsync(tag.TeamId, name, tagId, ct);
        var before = new { tag.Name, color = tag.Color.ToCode() };
        tag.Name = name;
        tag.Color = color;
        tag.UpdatedAt = clock.UtcNow;
        tag.UpdatedBy = access.UserId;
        audit.Add(new AuditEntry("tag.updated", TargetType: "tag", TargetId: tagId.ToString(), TeamId: tag.TeamId,
            Detail: new { from = before, to = new { name, color = color.ToCode() } }));
        await db.SaveChangesAsync(ct);
        return new TagDto(tag.Id, tag.TeamId, tag.Name, tag.Color);
    }

    public async Task DeleteAsync(Guid tagId, CancellationToken ct)
    {
        var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == tagId, ct) ?? throw new NotFoundException();
        await access.RequireTeamAsync(tag.TeamId, Operation.TeamTagManage, ct);

        // 付いているタスクからも外す
        var links = await db.TaskTags.Where(t => t.TagId == tagId).ToListAsync(ct);
        db.TaskTags.RemoveRange(links);
        db.Tags.Remove(tag);
        audit.Add(new AuditEntry("tag.deleted", TargetType: "tag", TargetId: tagId.ToString(), TeamId: tag.TeamId,
            Detail: new { tag.Name, removedFromTasks = links.Count }));
        await db.SaveChangesAsync(ct);
    }

    private static (string Name, TagColor Color) Validate(TagRequest request)
    {
        var v = new Validation();
        var name = v.SingleLine("name", request.Name, Limits.TagNameMax, required: true);
        var color = TagColor.Gray;
        if (request.Color is not null && v.TryCode<TagColor>("color", request.Color, out var c))
        {
            color = c.Value;
        }

        v.ThrowIfAny();
        return (name!, color);
    }

    private async Task EnsureUniqueAsync(Guid teamId, string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        if (await db.Tags.AnyAsync(t => t.TeamId == teamId && t.Name.ToLower() == lower && t.Id != exceptId, ct))
        {
            throw new RuleViolationException(Msg.TemTagDuplicate);
        }
    }
}
