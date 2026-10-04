namespace TaskYojitsu.Infrastructure.Persistence.Migrations;

/// <summary>
/// 最初のマイグレーションで実行する SQL（詳細設計書 3.2〜3.6）。EF Core で表せないものをここに書く。
/// マイグレーションは DB の所有者（tyj_owner）の権限で実行する。
/// </summary>
internal static class InitialCreateSql
{
    public static readonly string[] Up =
    [
        // 大文字と小文字を区別しない一意の索引
        "CREATE UNIQUE INDEX ux_teams_lower_name ON tyj.teams (lower(name));",
        "CREATE UNIQUE INDEX ux_tags_team_id_lower_name ON tyj.tags (team_id, lower(name));",

        // 監査ログのハッシュ連鎖の先頭（1 行だけ）
        """
        CREATE TABLE tyj.audit_chain_head (
            id smallint PRIMARY KEY CHECK (id = 1),
            last_id bigint NOT NULL,
            last_hash bytea NOT NULL,
            anchor_id bigint NOT NULL,
            anchor_hash bytea NOT NULL
        );
        """,
        "INSERT INTO tyj.audit_chain_head (id, last_id, last_hash, anchor_id, anchor_hash) VALUES (1, 0, decode(repeat('00', 32), 'hex'), 0, decode(repeat('00', 32), 'hex'));",

        // ハッシュの元になる本文。各項目を「|」でつないだ文字列（空の値は空文字。日時は UTC のマイクロ秒まで）
        """
        CREATE FUNCTION tyj.audit_log_body(a tyj.audit_logs) RETURNS text
        LANGUAGE sql STABLE
        SET search_path = tyj, pg_temp
        AS $$
            SELECT concat_ws('|',
                a.id::text,
                to_char(a.occurred_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                coalesce(a.actor_id::text, ''),
                coalesce(a.actor_hint, ''),
                a.action,
                a.result,
                coalesce(a.target_type, ''),
                coalesce(a.target_id, ''),
                coalesce(a.team_id::text, ''),
                coalesce(host(a.ip), ''),
                coalesce(a.user_agent, ''),
                coalesce(a.request_id, ''),
                coalesce(a.detail::text, ''))
        $$;
        """,

        // 追記のたびに、連鎖の先頭をロックしてから ID を振り、直前の記録のハッシュ値をつなぐ（同時の書き込みは順番に処理される）
        """
        CREATE FUNCTION tyj.audit_logs_chain() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER
        SET search_path = tyj, pg_temp
        AS $$
        DECLARE
            head tyj.audit_chain_head%ROWTYPE;
        BEGIN
            SELECT * INTO head FROM tyj.audit_chain_head WHERE id = 1 FOR UPDATE;
            NEW.id := head.last_id + 1;
            NEW.prev_hash := head.last_hash;
            NEW.hash := sha256(NEW.prev_hash || convert_to(tyj.audit_log_body(NEW), 'UTF8'));
            UPDATE tyj.audit_chain_head SET last_id = NEW.id, last_hash = NEW.hash WHERE id = 1;
            RETURN NEW;
        END;
        $$;
        """,
        "CREATE TRIGGER trg_audit_logs_chain BEFORE INSERT ON tyj.audit_logs FOR EACH ROW EXECUTE FUNCTION tyj.audit_logs_chain();",

        // 更新と削除は拒否する。保存期間を過ぎた記録の削除（tyj.audit_purge = on）だけを通す
        """
        CREATE FUNCTION tyj.audit_logs_protect() RETURNS trigger
        LANGUAGE plpgsql
        SET search_path = tyj, pg_temp
        AS $$
        BEGIN
            IF TG_OP = 'DELETE' AND coalesce(current_setting('tyj.audit_purge', true), '') = 'on' THEN
                RETURN OLD;
            END IF;
            RAISE EXCEPTION 'audit_logs は追記だけを許可しています（%）', TG_OP;
        END;
        $$;
        """,
        "CREATE TRIGGER trg_audit_logs_protect BEFORE UPDATE OR DELETE ON tyj.audit_logs FOR EACH ROW EXECUTE FUNCTION tyj.audit_logs_protect();",

        // 検証の起点（アプリのアカウントは連鎖の先頭のテーブルを直接読めないため、関数を通す）
        """
        CREATE FUNCTION tyj.audit_chain_anchor(OUT anchor_id bigint, OUT anchor_hash bytea)
        LANGUAGE sql STABLE SECURITY DEFINER
        SET search_path = tyj, pg_temp
        AS $$
            SELECT anchor_id, anchor_hash FROM tyj.audit_chain_head WHERE id = 1
        $$;
        """,
        "REVOKE ALL ON FUNCTION tyj.audit_chain_anchor() FROM PUBLIC;",

        // アプリのアカウントの権限（詳細設計書 3.4）。アカウントがある環境でだけ設定する
        """
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tyj_app') THEN
                GRANT USAGE ON SCHEMA tyj TO tyj_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tyj TO tyj_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA tyj TO tyj_app;
                REVOKE DELETE ON tyj.users, tyj.teams FROM tyj_app;
                REVOKE UPDATE, DELETE, TRUNCATE ON tyj.audit_logs FROM tyj_app;
                REVOKE ALL ON tyj.audit_chain_head FROM tyj_app;
                REVOKE INSERT, UPDATE, DELETE ON tyj.__ef_migrations_history FROM tyj_app;
                GRANT EXECUTE ON FUNCTION tyj.audit_chain_anchor() TO tyj_app;
            END IF;
        END
        $$;
        """,
    ];

    public static readonly string[] Down =
    [
        "DROP TRIGGER IF EXISTS trg_audit_logs_protect ON tyj.audit_logs;",
        "DROP TRIGGER IF EXISTS trg_audit_logs_chain ON tyj.audit_logs;",
        "DROP FUNCTION IF EXISTS tyj.audit_logs_protect();",
        "DROP FUNCTION IF EXISTS tyj.audit_logs_chain();",
        "DROP FUNCTION IF EXISTS tyj.audit_chain_anchor();",
        "DROP FUNCTION IF EXISTS tyj.audit_log_body(tyj.audit_logs);",
        "DROP TABLE IF EXISTS tyj.audit_chain_head;",
        "DROP INDEX IF EXISTS tyj.ux_tags_team_id_lower_name;",
        "DROP INDEX IF EXISTS tyj.ux_teams_lower_name;",
    ];
}
