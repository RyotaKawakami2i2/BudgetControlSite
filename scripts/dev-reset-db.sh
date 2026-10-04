#!/usr/bin/env bash
# 開発用の DB を空にして、マイグレーションを適用し直す（開発専用。本番では使わない）。
# 使い方: bash scripts/dev-reset-db.sh [--seed <デモのパスワード>]
set -euo pipefail
cd "$(dirname "$0")/.."

: "${ConnectionStrings__Owner:?ConnectionStrings__Owner が設定されていません（開発コンテナで実行してください）}"

# 接続文字列（Host=...;Database=...;Username=...;Password=...）から psql の引数を作る
declare -A cs
IFS=';' read -ra parts <<< "$ConnectionStrings__Owner"
for part in "${parts[@]}"; do
  [ -n "$part" ] && cs["${part%%=*}"]="${part#*=}"
done

echo "== スキーマ tyj を作り直します（${cs[Database]}）"
PGPASSWORD="${cs[Password]}" psql -h "${cs[Host]}" -p "${cs[Port]:-5432}" -U "${cs[Username]}" -d "${cs[Database]}" -v ON_ERROR_STOP=1 <<'SQL'
DROP SCHEMA IF EXISTS tyj CASCADE;
CREATE SCHEMA tyj AUTHORIZATION tyj_owner;
GRANT USAGE ON SCHEMA tyj TO tyj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO tyj_app;
ALTER DEFAULT PRIVILEGES FOR ROLE tyj_owner IN SCHEMA tyj GRANT USAGE, SELECT ON SEQUENCES TO tyj_app;
SQL

echo "== マイグレーションを適用します"
dotnet tool restore >/dev/null
dotnet ef database update --project src/TaskYojitsu.Infrastructure --startup-project src/TaskYojitsu.Infrastructure

if [ "${1:-}" = "--seed" ]; then
  echo "== デモデータを作ります"
  ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/TaskYojitsu.Tool -- seed-demo --password "${2:?パスワードを指定してください}"
fi

echo "== 完了"
