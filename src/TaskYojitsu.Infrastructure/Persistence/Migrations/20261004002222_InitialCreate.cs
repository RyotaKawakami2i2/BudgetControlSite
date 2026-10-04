using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TaskYojitsu.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tyj");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_hint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    detail = table.Column<string>(type: "jsonb", nullable: true),
                    prev_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.CheckConstraint("ck_audit_logs_result", "result IN ('success', 'failure', 'denied')");
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                schema: "tyj",
                columns: table => new
                {
                    holiday_date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.holiday_date);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    archived_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.CheckConstraint("ck_teams_name", "char_length(name) >= 1");
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    color = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                    table.UniqueConstraint("ak_tags_team_id_id", x => new { x.team_id, x.id });
                    table.CheckConstraint("ck_tags_color", "color IN ('gray', 'blue', 'green', 'yellow', 'orange', 'red', 'purple', 'teal')");
                    table.CheckConstraint("ck_tags_name", "char_length(name) >= 1");
                    table.ForeignKey(
                        name: "fk_tags_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "tyj",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comments", x => x.id);
                    table.CheckConstraint("ck_comments_body", "char_length(body) >= 1");
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_kind", "kind IN ('task_assigned', 'task_unassigned', 'comment_added', 'due_tomorrow', 'overdue', 'team_added')");
                    table.ForeignKey(
                        name: "fk_notifications_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "tyj",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    request_ip = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_views",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    conditions = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_views", x => x.id);
                    table.CheckConstraint("ck_saved_views_conditions", "octet_length(conditions::text) <= 4096");
                    table.CheckConstraint("ck_saved_views_shared", "NOT is_shared OR team_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_saved_views_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "tyj",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_admin = table.Column<bool>(type: "boolean", nullable: false),
                    default_view_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_totp_step = table.Column<long>(type: "bigint", nullable: true),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    disabled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    concurrency_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_active_has_password", "status <> 'active' OR password_hash IS NOT NULL");
                    table.CheckConstraint("ck_users_status", "status IN ('invited', 'active', 'disabled')");
                    table.ForeignKey(
                        name: "fk_users_saved_views_default_view_id",
                        column: x => x.default_view_id,
                        principalSchema: "tyj",
                        principalTable: "saved_views",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                schema: "tyj",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_members", x => new { x.team_id, x.user_id });
                    table.CheckConstraint("ck_team_members_role", "role IN ('leader', 'member')");
                    table.ForeignKey(
                        name: "fk_team_members_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "tyj",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_known_devices",
                schema: "tyj",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_known_devices", x => new { x.user_id, x.fingerprint });
                    table.ForeignKey(
                        name: "fk_user_known_devices_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "tyj",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_passkeys",
                schema: "tyj",
                columns: table => new
                {
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkeys", x => x.credential_id);
                    table.ForeignKey(
                        name: "fk_user_passkeys_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ticket = table.Column<byte[]>(type: "bytea", nullable: false),
                    auth_method = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    auth_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip = table.Column<IPAddress>(type: "inet", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_auth_method", "auth_method IN ('password', 'password_totp', 'password_recovery', 'passkey')");
                    table.CheckConstraint("ck_user_sessions_revoked_reason", "revoked_reason IS NULL OR revoked_reason IN ('logout', 'password_changed', 'mfa_changed', 'disabled', 'user_revoked', 'admin_revoked')");
                    table.ForeignKey(
                        name: "fk_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "tyj",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tasks",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depth = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    priority = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    planned_start = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_end = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_minutes = table.Column<int>(type: "integer", nullable: true),
                    actual_start = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end = table.Column<DateOnly>(type: "date", nullable: true),
                    progress = table.Column<short>(type: "smallint", nullable: false),
                    is_milestone = table.Column<bool>(type: "boolean", nullable: false),
                    result_note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    delete_batch_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tasks", x => x.id);
                    table.UniqueConstraint("ak_tasks_team_id_id", x => new { x.team_id, x.id });
                    table.CheckConstraint("ck_tasks_actual_order", "actual_end >= actual_start");
                    table.CheckConstraint("ck_tasks_depth", "depth BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_tasks_done", "status <> 'done' OR (actual_end IS NOT NULL AND progress = 100)");
                    table.CheckConstraint("ck_tasks_milestone", "NOT is_milestone OR (planned_start = planned_end AND COALESCE(planned_minutes, 0) = 0)");
                    table.CheckConstraint("ck_tasks_planned_minutes", "planned_minutes BETWEEN 0 AND 599940 AND planned_minutes % 15 = 0");
                    table.CheckConstraint("ck_tasks_planned_order", "planned_end >= planned_start");
                    table.CheckConstraint("ck_tasks_planned_pair", "(planned_start IS NULL) = (planned_end IS NULL)");
                    table.CheckConstraint("ck_tasks_priority", "priority IN ('high', 'medium', 'low')");
                    table.CheckConstraint("ck_tasks_progress", "progress BETWEEN 0 AND 100 AND progress % 5 = 0");
                    table.CheckConstraint("ck_tasks_status", "status IN ('not_started', 'in_progress', 'on_hold', 'done', 'cancelled')");
                    table.CheckConstraint("ck_tasks_title", "char_length(title) >= 1");
                    table.ForeignKey(
                        name: "fk_tasks_tasks_team_id_parent_id",
                        columns: x => new { x.team_id, x.parent_id },
                        principalSchema: "tyj",
                        principalTable: "tasks",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tasks_team_members_team_id_assignee_id",
                        columns: x => new { x.team_id, x.assignee_id },
                        principalSchema: "tyj",
                        principalTable: "team_members",
                        principalColumns: new[] { "team_id", "user_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "tyj",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tasks_users_created_by",
                        column: x => x.created_by,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "task_dependencies",
                schema: "tyj",
                columns: table => new
                {
                    predecessor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    successor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_dependencies", x => new { x.predecessor_id, x.successor_id });
                    table.CheckConstraint("ck_task_dependencies_self", "predecessor_id <> successor_id");
                    table.ForeignKey(
                        name: "fk_task_dependencies_tasks_team_id_predecessor_id",
                        columns: x => new { x.team_id, x.predecessor_id },
                        principalSchema: "tyj",
                        principalTable: "tasks",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_dependencies_tasks_team_id_successor_id",
                        columns: x => new { x.team_id, x.successor_id },
                        principalSchema: "tyj",
                        principalTable: "tasks",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_histories",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    field = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    old_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_histories", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_histories_tasks_team_id_task_id",
                        columns: x => new { x.team_id, x.task_id },
                        principalSchema: "tyj",
                        principalTable: "tasks",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "task_tags",
                schema: "tyj",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_tags", x => new { x.task_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_task_tags_tags_team_id_tag_id",
                        columns: x => new { x.team_id, x.tag_id },
                        principalSchema: "tyj",
                        principalTable: "tags",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_task_tags_tasks_team_id_task_id",
                        columns: x => new { x.team_id, x.task_id },
                        principalSchema: "tyj",
                        principalTable: "tasks",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_logs",
                schema: "tyj",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_logs", x => x.id);
                    table.CheckConstraint("ck_work_logs_minutes", "minutes BETWEEN 15 AND 1440 AND minutes % 15 = 0");
                    table.CheckConstraint("ck_work_logs_source", "source IN ('dialog', 'timesheet')");
                    table.ForeignKey(
                        name: "fk_work_logs_tasks_team_id_task_id",
                        columns: x => new { x.team_id, x.task_id },
                        principalSchema: "tyj",
                        principalTable: "tasks",
                        principalColumns: new[] { "team_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_logs_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "tyj",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action_occurred_at",
                schema: "tyj",
                table: "audit_logs",
                columns: new[] { "action", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_id_occurred_at",
                schema: "tyj",
                table: "audit_logs",
                columns: new[] { "actor_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_occurred_at",
                schema: "tyj",
                table: "audit_logs",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_target_type_target_id",
                schema: "tyj",
                table: "audit_logs",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_comments_author_id",
                schema: "tyj",
                table: "comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_comments_task_id_created_at",
                schema: "tyj",
                table: "comments",
                columns: new[] { "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_comments_team_id_task_id",
                schema: "tyj",
                table: "comments",
                columns: new[] { "team_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_user_id",
                schema: "tyj",
                table: "invitations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_invitations_token_hash",
                schema: "tyj",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_task_id",
                schema: "tyj",
                table: "notifications",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_team_id",
                schema: "tyj",
                table: "notifications",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                schema: "tyj",
                table: "notifications",
                column: "user_id",
                filter: "read_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at",
                schema: "tyj",
                table: "notifications",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id",
                schema: "tyj",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_password_reset_tokens_token_hash",
                schema: "tyj",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_views_owner_id",
                schema: "tyj",
                table: "saved_views",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_views_team_id",
                schema: "tyj",
                table: "saved_views",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_successor_id",
                schema: "tyj",
                table: "task_dependencies",
                column: "successor_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_team_id",
                schema: "tyj",
                table: "task_dependencies",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_team_id_predecessor_id",
                schema: "tyj",
                table: "task_dependencies",
                columns: new[] { "team_id", "predecessor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_dependencies_team_id_successor_id",
                schema: "tyj",
                table: "task_dependencies",
                columns: new[] { "team_id", "successor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_histories_task_id_occurred_at",
                schema: "tyj",
                table: "task_histories",
                columns: new[] { "task_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_task_histories_team_id_task_id",
                schema: "tyj",
                table: "task_histories",
                columns: new[] { "team_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_tag_id",
                schema: "tyj",
                table: "task_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_team_id",
                schema: "tyj",
                table: "task_tags",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_team_id_tag_id",
                schema: "tyj",
                table: "task_tags",
                columns: new[] { "team_id", "tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_tags_team_id_task_id",
                schema: "tyj",
                table: "task_tags",
                columns: new[] { "team_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_assignee_id",
                schema: "tyj",
                table: "tasks",
                column: "assignee_id",
                filter: "deleted_at IS NULL AND status NOT IN ('done', 'cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_created_by",
                schema: "tyj",
                table: "tasks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_delete_batch_id",
                schema: "tyj",
                table: "tasks",
                column: "delete_batch_id",
                filter: "delete_batch_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_parent_id",
                schema: "tyj",
                table: "tasks",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_team_id_assignee_id",
                schema: "tyj",
                table: "tasks",
                columns: new[] { "team_id", "assignee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_team_id_parent_id_sort_order",
                schema: "tyj",
                table: "tasks",
                columns: new[] { "team_id", "parent_id", "sort_order" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_team_id_planned_start_planned_end",
                schema: "tyj",
                table: "tasks",
                columns: new[] { "team_id", "planned_start", "planned_end" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_team_members_user_id",
                schema: "tyj",
                table: "team_members",
                column: "user_id",
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "tyj",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "tyj",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_user_id",
                schema: "tyj",
                table: "user_passkeys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id_revoked_at",
                schema: "tyj",
                table: "user_sessions",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ux_user_sessions_key_hash",
                schema: "tyj",
                table: "user_sessions",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_default_view_id",
                schema: "tyj",
                table: "users",
                column: "default_view_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                schema: "tyj",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_user_name",
                schema: "tyj",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_logs_task_id",
                schema: "tyj",
                table: "work_logs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_logs_team_id",
                schema: "tyj",
                table: "work_logs",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_logs_team_id_task_id",
                schema: "tyj",
                table: "work_logs",
                columns: new[] { "team_id", "task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_logs_user_id_work_date",
                schema: "tyj",
                table: "work_logs",
                columns: new[] { "user_id", "work_date" });

            migrationBuilder.AddForeignKey(
                name: "fk_comments_tasks_team_id_task_id",
                schema: "tyj",
                table: "comments",
                columns: new[] { "team_id", "task_id" },
                principalSchema: "tyj",
                principalTable: "tasks",
                principalColumns: new[] { "team_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_comments_users_author_id",
                schema: "tyj",
                table: "comments",
                column: "author_id",
                principalSchema: "tyj",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invitations_users_user_id",
                schema: "tyj",
                table: "invitations",
                column: "user_id",
                principalSchema: "tyj",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_tasks_task_id",
                schema: "tyj",
                table: "notifications",
                column: "task_id",
                principalSchema: "tyj",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_users_user_id",
                schema: "tyj",
                table: "notifications",
                column: "user_id",
                principalSchema: "tyj",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_password_reset_tokens_users_user_id",
                schema: "tyj",
                table: "password_reset_tokens",
                column: "user_id",
                principalSchema: "tyj",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_saved_views_users_owner_id",
                schema: "tyj",
                table: "saved_views",
                column: "owner_id",
                principalSchema: "tyj",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // EF Core で表せないもの（式の索引、監査ログのトリガー、権限）
            foreach (var sql in InitialCreateSql.Up)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in InitialCreateSql.Down)
            {
                migrationBuilder.Sql(sql);
            }

            migrationBuilder.DropForeignKey(
                name: "fk_saved_views_users_owner_id",
                schema: "tyj",
                table: "saved_views");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "comments",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "holidays",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "invitations",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "password_reset_tokens",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "task_dependencies",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "task_histories",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "task_tags",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "user_known_devices",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "user_passkeys",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "user_sessions",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "work_logs",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "tasks",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "team_members",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "users",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "saved_views",
                schema: "tyj");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "tyj");
        }
    }
}
