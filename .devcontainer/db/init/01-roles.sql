-- 開発用の DB の初期設定（開発専用。パスワードは開発用の固定値）
-- 本番と同じく、所有者（マイグレーション用）とアプリ用のアカウントを分ける（詳細設計書 3.4）。
-- テーブルごとの細かい権限（監査ログは追記だけ、など）は、マイグレーションで設定する。

CREATE ROLE tyj_owner LOGIN PASSWORD 'dev-owner-password';
CREATE ROLE tyj_app LOGIN PASSWORD 'dev-app-password';
ALTER ROLE tyj_app SET statement_timeout = '30s';
ALTER ROLE tyj_app SET idle_in_transaction_session_timeout = '60s';

CREATE DATABASE taskyojitsu OWNER tyj_owner;
CREATE DATABASE taskyojitsu_test OWNER tyj_owner;

-- アプリの DB
\connect taskyojitsu
REVOKE ALL ON DATABASE taskyojitsu FROM PUBLIC;
GRANT CONNECT ON DATABASE taskyojitsu TO tyj_app;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA tyj AUTHORIZATION tyj_owner;
GRANT USAGE ON SCHEMA tyj TO tyj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tyj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj
    GRANT USAGE, SELECT ON SEQUENCES TO tyj_app;

-- 結合テスト用の DB（アプリの DB と同じ設定）
\connect taskyojitsu_test
REVOKE ALL ON DATABASE taskyojitsu_test FROM PUBLIC;
GRANT CONNECT ON DATABASE taskyojitsu_test TO tyj_app;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA tyj AUTHORIZATION tyj_owner;
GRANT USAGE ON SCHEMA tyj TO tyj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tyj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj
    GRANT USAGE, SELECT ON SEQUENCES TO tyj_app;
