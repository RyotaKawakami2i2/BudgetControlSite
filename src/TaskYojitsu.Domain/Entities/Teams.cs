using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Entities;

/// <summary>チーム（teams）。プロジェクトの単位を兼ねる。削除せずアーカイブする。</summary>
public class Team
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid? ArchivedBy { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsArchived => ArchivedAt is not null;
}

/// <summary>チームの所属（team_members）。チームから外した後も行を残す（removed_at）。</summary>
public class TeamMember
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public TeamRole Role { get; set; } = TeamRole.Member;
    public DateTime JoinedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }

    public bool IsCurrent => RemovedAt is null;
}

/// <summary>タグ（tags）。</summary>
public class Tag
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; } = "";
    public TagColor Color { get; set; } = TagColor.Gray;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
