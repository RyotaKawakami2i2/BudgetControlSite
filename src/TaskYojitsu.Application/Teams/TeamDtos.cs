using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Application.Teams;

/// <summary>チームの一覧の 1 件（API-06）。role は leader、member、admin_view のいずれか。</summary>
public sealed record TeamSummaryDto(Guid Id, string Name, string? Description, bool Archived, string Role, int MemberCount, int Version);

public sealed record TeamMemberDto(Guid UserId, string DisplayName, string? Email, TeamRole Role, DateTime JoinedAt, bool Disabled);

public sealed record TeamCan(
    bool Update,
    bool Archive,
    bool Unarchive,
    bool ManageMembers,
    bool ManageTags,
    bool ManageSharedViews,
    bool ViewReport,
    bool CreateTask,
    bool Restore);

/// <summary>チームの基本情報、メンバー、タグ（API-08）。</summary>
public sealed record TeamDetailDto(
    Guid Id,
    string Name,
    string? Description,
    bool Archived,
    DateTime? ArchivedAt,
    int Version,
    string Role,
    IReadOnlyList<TeamMemberDto> Members,
    IReadOnlyList<TagDto> Tags,
    TeamCan Can);

/// <summary>チームの作成（API-07。詳細設計書 5.4.6）。管理者が作るときは leaderUserId を指定する。</summary>
public sealed record CreateTeamRequest(string? Name, string? Description, Guid? LeaderUserId);

public sealed record UpdateTeamRequest
{
    public int Version { get; init; }
    public Optional<string?> Name { get; init; }
    public Optional<string?> Description { get; init; }
}

public sealed record VersionRequest(int? Version);

public sealed record AddMemberRequest(Guid UserId, string? Role);

public sealed record ChangeRoleRequest(string? Role);

public sealed record UserSearchDto(Guid Id, string DisplayName, string Email);

public sealed record TagRequest(string? Name, string? Color);
