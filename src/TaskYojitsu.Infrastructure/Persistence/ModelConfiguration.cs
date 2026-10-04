using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Infrastructure.Persistence;

/// <summary>
/// テーブルの定義（詳細設計書 3.2、3.3）。EF Core で表せないもの（式を使った索引、複合外部キー、トリガー、権限）は
/// マイグレーションの中で SQL を書く（3.6）。
/// </summary>
internal static class ModelConfiguration
{
    private const string Now = "now()";

    public static void Apply(ModelBuilder b)
    {
        ConfigureIdentity(b);
        ConfigureSecurity(b);
        ConfigureTeams(b);
        ConfigureTasks(b);
        ConfigureMisc(b);
    }

    private static void ConfigureIdentity(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_status", "status IN ('invited', 'active', 'disabled')");
                t.HasCheckConstraint("ck_users_active_has_password", "status <> 'active' OR password_hash IS NOT NULL");
            });
            e.Property(u => u.UserName).HasMaxLength(256).IsRequired();
            e.Property(u => u.NormalizedUserName).HasMaxLength(256).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
            e.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
            e.Property(u => u.ConcurrencyStamp).HasMaxLength(64).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(50).IsRequired();
            e.Property(u => u.Status).IsRequired();
            e.Property(u => u.CreatedAt).HasDefaultValueSql(Now);
            e.Property(u => u.UpdatedAt).HasDefaultValueSql(Now);
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
            e.HasIndex(u => u.NormalizedUserName).IsUnique();
            e.HasOne<SavedView>().WithMany().HasForeignKey(u => u.DefaultViewId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        b.Entity<IdentityUserPasskey<Guid>>().ToTable("user_passkeys");
        b.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");

        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }

    private static void ConfigureSecurity(ModelBuilder b)
    {
        b.Entity<UserSession>(e =>
        {
            e.ToTable("user_sessions", t =>
            {
                t.HasCheckConstraint("ck_user_sessions_auth_method", "auth_method IN ('password', 'password_totp', 'password_recovery', 'passkey')");
                t.HasCheckConstraint("ck_user_sessions_revoked_reason",
                    "revoked_reason IS NULL OR revoked_reason IN ('logout', 'password_changed', 'mfa_changed', 'disabled', 'user_revoked', 'admin_revoked')");
            });
            e.HasKey(s => s.Id);
            e.Property(s => s.KeyHash).IsRequired();
            e.Property(s => s.Ticket).IsRequired();
            e.Property(s => s.UserAgent).HasMaxLength(256).IsRequired();
            e.Property(s => s.CreatedAt).HasDefaultValueSql(Now);
            e.Property(s => s.LastSeenAt).HasDefaultValueSql(Now);
            e.HasIndex(s => s.KeyHash).IsUnique();
            e.HasIndex(s => new { s.UserId, s.RevokedAt });
            e.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserKnownDevice>(e =>
        {
            e.ToTable("user_known_devices");
            e.HasKey(d => new { d.UserId, d.Fingerprint });
            e.Property(d => d.FirstSeenAt).HasDefaultValueSql(Now);
            e.Property(d => d.LastSeenAt).HasDefaultValueSql(Now);
            e.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Invitation>(e =>
        {
            e.ToTable("invitations");
            e.HasKey(i => i.Id);
            e.Property(i => i.TokenHash).IsRequired();
            e.Property(i => i.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(i => i.TokenHash).IsUnique();
            e.HasIndex(i => i.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).IsRequired();
            e.Property(t => t.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTeams(ModelBuilder b)
    {
        b.Entity<Team>(e =>
        {
            e.ToTable("teams", t => t.HasCheckConstraint("ck_teams_name", "char_length(name) >= 1"));
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(50).IsRequired();
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.Version).HasDefaultValue(1).IsConcurrencyToken();
            e.Property(t => t.CreatedAt).HasDefaultValueSql(Now);
            e.Property(t => t.UpdatedAt).HasDefaultValueSql(Now);
            e.Ignore(t => t.IsArchived);
        });

        b.Entity<TeamMember>(e =>
        {
            e.ToTable("team_members", t => t.HasCheckConstraint("ck_team_members_role", "role IN ('leader', 'member')"));
            e.HasKey(m => new { m.TeamId, m.UserId });
            e.Property(m => m.JoinedAt).HasDefaultValueSql(Now);
            e.HasIndex(m => m.UserId).HasFilter("removed_at IS NULL");
            e.HasOne<Team>().WithMany().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(m => m.IsCurrent);
        });

        b.Entity<Tag>(e =>
        {
            e.ToTable("tags", t =>
            {
                t.HasCheckConstraint("ck_tags_color", "color IN ('gray', 'blue', 'green', 'yellow', 'orange', 'red', 'purple', 'teal')");
                t.HasCheckConstraint("ck_tags_name", "char_length(name) >= 1");
            });
            e.HasKey(t => t.Id);
            e.HasAlternateKey(t => new { t.TeamId, t.Id });
            e.Property(t => t.Name).HasMaxLength(30).IsRequired();
            e.Property(t => t.CreatedAt).HasDefaultValueSql(Now);
            e.Property(t => t.UpdatedAt).HasDefaultValueSql(Now);
            e.HasOne<Team>().WithMany().HasForeignKey(t => t.TeamId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTasks(ModelBuilder b)
    {
        b.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks", t =>
            {
                t.HasCheckConstraint("ck_tasks_depth", "depth BETWEEN 1 AND 4");
                t.HasCheckConstraint("ck_tasks_title", "char_length(title) >= 1");
                t.HasCheckConstraint("ck_tasks_status", "status IN ('not_started', 'in_progress', 'on_hold', 'done', 'cancelled')");
                t.HasCheckConstraint("ck_tasks_priority", "priority IN ('high', 'medium', 'low')");
                t.HasCheckConstraint("ck_tasks_planned_pair", "(planned_start IS NULL) = (planned_end IS NULL)");
                t.HasCheckConstraint("ck_tasks_planned_order", "planned_end >= planned_start");
                t.HasCheckConstraint("ck_tasks_planned_minutes", "planned_minutes BETWEEN 0 AND 599940 AND planned_minutes % 15 = 0");
                t.HasCheckConstraint("ck_tasks_actual_order", "actual_end >= actual_start");
                t.HasCheckConstraint("ck_tasks_progress", "progress BETWEEN 0 AND 100 AND progress % 5 = 0");
                t.HasCheckConstraint("ck_tasks_milestone", "NOT is_milestone OR (planned_start = planned_end AND COALESCE(planned_minutes, 0) = 0)");
                t.HasCheckConstraint("ck_tasks_done", "status <> 'done' OR (actual_end IS NOT NULL AND progress = 100)");
            });
            e.HasKey(t => t.Id);
            e.HasAlternateKey(t => new { t.TeamId, t.Id });
            e.Property(t => t.Depth).HasDefaultValue((short)1);
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.Description).HasMaxLength(4000);
            e.Property(t => t.ResultNote).HasMaxLength(4000);
            e.Property(t => t.Version).HasDefaultValue(1).IsConcurrencyToken();
            e.Property(t => t.CreatedAt).HasDefaultValueSql(Now);
            e.Property(t => t.UpdatedAt).HasDefaultValueSql(Now);
            e.HasOne<Team>().WithMany().HasForeignKey(t => t.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedBy).OnDelete(DeleteBehavior.Restrict);

            // team_id を含む複合外部キー。親は同じチームのタスク、担当者はチームの所属者であることを DB で保証する（基本設計書 5.4）
            e.HasOne<TaskItem>().WithMany().HasForeignKey(t => new { t.TeamId, t.ParentId })
                .HasPrincipalKey(p => new { p.TeamId, p.Id }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TeamMember>().WithMany().HasForeignKey(t => new { t.TeamId, t.AssigneeId })
                .HasPrincipalKey(m => new { m.TeamId, m.UserId }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => new { t.TeamId, t.ParentId, t.SortOrder }).HasFilter("deleted_at IS NULL");
            e.HasIndex(t => t.AssigneeId).HasFilter("deleted_at IS NULL AND status NOT IN ('done', 'cancelled')");
            e.HasIndex(t => new { t.TeamId, t.PlannedStart, t.PlannedEnd }).HasFilter("deleted_at IS NULL");
            e.HasIndex(t => t.ParentId);
            e.HasIndex(t => t.DeleteBatchId).HasFilter("delete_batch_id IS NOT NULL");
            e.HasIndex(t => t.CreatedBy);
        });

        b.Entity<TaskDependency>(e =>
        {
            e.ToTable("task_dependencies", t => t.HasCheckConstraint("ck_task_dependencies_self", "predecessor_id <> successor_id"));
            e.HasKey(d => new { d.PredecessorId, d.SuccessorId });
            e.Property(d => d.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(d => d.SuccessorId);
            e.HasIndex(d => d.TeamId);
            TaskForeignKey(e, d => new { d.TeamId, d.PredecessorId });
            TaskForeignKey(e, d => new { d.TeamId, d.SuccessorId });
        });

        b.Entity<TaskTag>(e =>
        {
            e.ToTable("task_tags");
            e.HasKey(t => new { t.TaskId, t.TagId });
            e.HasIndex(t => t.TagId);
            e.HasIndex(t => t.TeamId);
            TaskForeignKey(e, t => new { t.TeamId, t.TaskId });
            e.HasOne<Tag>().WithMany().HasForeignKey(t => new { t.TeamId, t.TagId })
                .HasPrincipalKey(tag => new { tag.TeamId, tag.Id }).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskHistory>(e =>
        {
            e.ToTable("task_histories");
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).UseIdentityAlwaysColumn();
            e.Property(h => h.OccurredAt).HasDefaultValueSql(Now);
            e.Property(h => h.Field).HasMaxLength(32);
            e.Property(h => h.OldValue).HasMaxLength(4000);
            e.Property(h => h.NewValue).HasMaxLength(4000);
            e.HasIndex(h => new { h.TaskId, h.OccurredAt });
            TaskForeignKey(e, h => new { h.TeamId, h.TaskId });
        });

        b.Entity<Comment>(e =>
        {
            e.ToTable("comments", t => t.HasCheckConstraint("ck_comments_body", "char_length(body) >= 1"));
            e.HasKey(c => c.Id);
            e.Property(c => c.Body).HasMaxLength(2000).IsRequired();
            e.Property(c => c.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(c => new { c.TaskId, c.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
            TaskForeignKey(e, c => new { c.TeamId, c.TaskId });
        });

        b.Entity<WorkLog>(e =>
        {
            e.ToTable("work_logs", t =>
            {
                t.HasCheckConstraint("ck_work_logs_minutes", "minutes BETWEEN 15 AND 1440 AND minutes % 15 = 0");
                t.HasCheckConstraint("ck_work_logs_source", "source IN ('dialog', 'timesheet')");
            });
            e.HasKey(w => w.Id);
            e.Property(w => w.Note).HasMaxLength(500);
            e.Property(w => w.Version).HasDefaultValue(1).IsConcurrencyToken();
            e.Property(w => w.CreatedAt).HasDefaultValueSql(Now);
            e.Property(w => w.UpdatedAt).HasDefaultValueSql(Now);
            e.HasIndex(w => w.TaskId);
            e.HasIndex(w => new { w.UserId, w.WorkDate });
            e.HasIndex(w => w.TeamId);
            e.HasOne<User>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Restrict);
            TaskForeignKey(e, w => new { w.TeamId, w.TaskId });
        });
    }

    /// <summary>(team_id, task_id) から tasks(team_id, id) への外部キー。タスクを完全に削除したら一緒に消える。</summary>
    private static void TaskForeignKey<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e, System.Linq.Expressions.Expression<Func<T, object?>> keys)
        where T : class =>
        e.HasOne<TaskItem>().WithMany().HasForeignKey(keys).HasPrincipalKey(t => new { t.TeamId, t.Id }).OnDelete(DeleteBehavior.Cascade);

    private static void ConfigureMisc(ModelBuilder b)
    {
        b.Entity<SavedView>(e =>
        {
            e.ToTable("saved_views", t =>
            {
                t.HasCheckConstraint("ck_saved_views_shared", "NOT is_shared OR team_id IS NOT NULL");
                t.HasCheckConstraint("ck_saved_views_conditions", "octet_length(conditions::text) <= 4096");
            });
            e.HasKey(v => v.Id);
            e.Property(v => v.Name).HasMaxLength(50).IsRequired();
            e.Property(v => v.Conditions).HasColumnType("jsonb").IsRequired();
            e.Property(v => v.Version).HasDefaultValue(1).IsConcurrencyToken();
            e.Property(v => v.CreatedAt).HasDefaultValueSql(Now);
            e.Property(v => v.UpdatedAt).HasDefaultValueSql(Now);
            e.HasIndex(v => v.OwnerId);
            e.HasIndex(v => v.TeamId);
            e.HasOne<User>().WithMany().HasForeignKey(v => v.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Team>().WithMany().HasForeignKey(v => v.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications", t => t.HasCheckConstraint("ck_notifications_kind",
                "kind IN ('task_assigned', 'task_unassigned', 'comment_added', 'due_tomorrow', 'overdue', 'team_added')"));
            e.HasKey(n => n.Id);
            e.Property(n => n.CreatedAt).HasDefaultValueSql(Now);
            e.HasIndex(n => new { n.UserId, n.CreatedAt }).IsDescending(false, true);
            e.HasIndex(n => n.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
            e.HasIndex(n => n.TaskId);
            e.HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(n => n.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Team>().WithMany().HasForeignKey(n => n.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Holiday>(e =>
        {
            e.ToTable("holidays");
            e.HasKey(h => h.HolidayDate);
            e.Property(h => h.Name).HasMaxLength(50).IsRequired();
            e.Property(h => h.CreatedAt).HasDefaultValueSql(Now);
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs", t => t.HasCheckConstraint("ck_audit_logs_result", "result IN ('success', 'failure', 'denied')"));
            e.HasKey(a => a.Id);

            // ID はトリガーが連鎖の先頭をロックしてから振る（連鎖の順と ID の順をそろえるため。マイグレーションの SQL を参照）
            e.Property(a => a.Id).UseIdentityByDefaultColumn();
            e.Property(a => a.OccurredAt).HasDefaultValueSql(Now);
            e.Property(a => a.ActorHint).HasMaxLength(64);
            e.Property(a => a.Action).HasMaxLength(64).IsRequired();
            e.Property(a => a.TargetType).HasMaxLength(32);
            e.Property(a => a.TargetId).HasMaxLength(64);
            e.Property(a => a.UserAgent).HasMaxLength(256);
            e.Property(a => a.RequestId).HasMaxLength(64);
            e.Property(a => a.Detail).HasColumnType("jsonb");

            // prev_hash と hash はトリガーが設定する（アプリからは送らない）
            e.Property(a => a.PrevHash).IsRequired().ValueGeneratedOnAdd();
            e.Property(a => a.Hash).IsRequired().ValueGeneratedOnAdd();
            e.HasIndex(a => a.OccurredAt);
            e.HasIndex(a => new { a.ActorId, a.OccurredAt });
            e.HasIndex(a => new { a.TargetType, a.TargetId });
            e.HasIndex(a => new { a.Action, a.OccurredAt });
        });
    }
}
