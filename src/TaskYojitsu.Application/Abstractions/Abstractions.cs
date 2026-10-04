using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Application.Abstractions;

/// <summary>DB へのアクセス。実装は Infrastructure の AppDbContext。</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<Tag> Tags { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<TaskDependency> TaskDependencies { get; }
    DbSet<TaskTag> TaskTags { get; }
    DbSet<TaskHistory> TaskHistories { get; }
    DbSet<Comment> Comments { get; }
    DbSet<WorkLog> WorkLogs { get; }
    DbSet<SavedView> SavedViews { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<UserSession> UserSessions { get; }
    DbSet<UserKnownDevice> UserKnownDevices { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    /// <summary>パスキー（Identity の標準のテーブル）。登録数の確認にだけ使う。</summary>
    DbSet<Microsoft.AspNetCore.Identity.IdentityUserPasskey<Guid>> UserPasskeys { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>DB のロック（トランザクションの終わりに自動で外れる）。</summary>
public interface IDbLocks
{
    /// <summary>チームの階層の変更（移動、依存関係の追加）を順番に処理するためのロック（詳細設計書 4.8）。</summary>
    Task LockTeamHierarchyAsync(Guid teamId, CancellationToken cancellationToken);

    /// <summary>利用者と作業日の組み合わせのロック（1 日の上限の確認。詳細設計書 4.10）。</summary>
    Task LockUserDayAsync(Guid userId, DateOnly workDate, CancellationToken cancellationToken);

    /// <summary>名前を付けたロック（最後の管理者・最後のリーダーの確認など）。</summary>
    Task LockAsync(string name, CancellationToken cancellationToken);
}

/// <summary>要求の情報（操作者、送信元など）。</summary>
public interface IRequestContext
{
    /// <summary>ログインしている利用者。未ログインなら null。</summary>
    Guid? UserId { get; }

    /// <summary>セッションの ID（user_sessions.id）。</summary>
    Guid? SessionId { get; }

    /// <summary>最後に認証した日時（再認証を含む）。</summary>
    DateTime? AuthTime { get; }

    IPAddress? Ip { get; }

    string? UserAgent { get; }

    string? RequestId { get; }

    /// <summary>管理の操作を許可したネットワークからの要求か（設定がなければ true）。</summary>
    bool IsFromAdminNetwork { get; }
}

/// <summary>監査ログの記録の内容。</summary>
public sealed record AuditEntry(
    string Action,
    AuditResult Result = AuditResult.Success,
    string? TargetType = null,
    string? TargetId = null,
    Guid? TeamId = null,
    object? Detail = null,
    Guid? ActorId = null,
    string? ActorHint = null);

/// <summary>監査ログの書き込み（NF-LOG-01〜05）。</summary>
public interface IAuditWriter
{
    /// <summary>業務データの変更と同じトランザクションで記録する（SaveChanges で一緒に保存される）。</summary>
    void Add(AuditEntry entry);

    /// <summary>すぐに記録する（ログインの失敗や権限の拒否など、業務データの変更を伴わないもの）。</summary>
    Task WriteNowAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>メールの送信。本文はテキストだけ。</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
