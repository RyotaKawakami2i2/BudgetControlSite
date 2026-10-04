using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Infrastructure.Persistence;

/// <summary>
/// DB のコンテキスト。Identity のユーザー情報（ロールは使わない）と業務のテーブルを、スキーマ tyj に置く（詳細設計書 3.1）。
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<User, Guid, IdentityUserClaim<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>, IdentityUserPasskey<Guid>>(options),
      IAppDbContext,
      IDataProtectionKeyContext
{
    public const string Schema = "tyj";

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskTag> TaskTags => Set<TaskTag>();
    public DbSet<TaskHistory> TaskHistories => Set<TaskHistory>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<WorkLog> WorkLogs => Set<WorkLog>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserKnownDevice> UserKnownDevices => Set<UserKnownDevice>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    DbSet<IdentityUserPasskey<Guid>> IAppDbContext.UserPasskeys => UserPasskeys;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // 区分値は英字のコードで保存する（DATA-04）
        configurationBuilder.Properties<TaskItemStatus>().HaveConversion<EnumCodeConverter<TaskItemStatus>>().HaveMaxLength(16);
        configurationBuilder.Properties<Priority>().HaveConversion<EnumCodeConverter<Priority>>().HaveMaxLength(8);
        configurationBuilder.Properties<TeamRole>().HaveConversion<EnumCodeConverter<TeamRole>>().HaveMaxLength(8);
        configurationBuilder.Properties<UserStatus>().HaveConversion<EnumCodeConverter<UserStatus>>().HaveMaxLength(16);
        configurationBuilder.Properties<AuthMethod>().HaveConversion<EnumCodeConverter<AuthMethod>>().HaveMaxLength(24);
        configurationBuilder.Properties<NotificationKind>().HaveConversion<EnumCodeConverter<NotificationKind>>().HaveMaxLength(24);
        configurationBuilder.Properties<TagColor>().HaveConversion<EnumCodeConverter<TagColor>>().HaveMaxLength(8);
        configurationBuilder.Properties<WorkLogSource>().HaveConversion<EnumCodeConverter<WorkLogSource>>().HaveMaxLength(12);
        configurationBuilder.Properties<TaskHistoryKind>().HaveConversion<EnumCodeConverter<TaskHistoryKind>>().HaveMaxLength(24);
        configurationBuilder.Properties<SessionRevokeReason>().HaveConversion<EnumCodeConverter<SessionRevokeReason>>().HaveMaxLength(32);
        configurationBuilder.Properties<AuditResult>().HaveConversion<EnumCodeConverter<AuditResult>>().HaveMaxLength(8);
    }

    /// <remarks>
    /// Identity のテーブルは、パスキーを含む版（Version3）で作る。版は IdentityOptions.Stores.SchemaVersion で決まるため、
    /// アプリ・運用コマンド・設計時のいずれでも Version3 を設定する（DbSetup.IdentityOptionsForDesignTime）。
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        ModelConfiguration.Apply(builder);
        NamingConvention.ApplySnakeCase(builder);
    }
}
