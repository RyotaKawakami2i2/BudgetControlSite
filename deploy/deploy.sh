#!/usr/bin/env bash
# 配備スクリプト（詳細設計書 12.7）。運用担当が、配備する版（digest）を指定して本番のサーバーで実行する。
#   使い方: sudo deploy/deploy.sh ghcr.io/<組織>/taskyojitsu@sha256:<digest>
# 前提:
#   - /etc/tyj/deploy.env に TYJ_BASE_URL、TYJ_DB_IMAGE などを書いてある（compose.yaml を参照）
#   - /etc/tyj/cosign.pub に、イメージの署名を検証する公開鍵がある（鍵の組による署名。公開の記録には問い合わせない）
#   - cosign、docker（compose）、jq が入っている
set -euo pipefail

readonly IMAGE="${1:?配備するイメージを digest 付きで指定してください（例: ghcr.io/<組織>/taskyojitsu@sha256:...）}"
readonly ROOT="$(cd "$(dirname "$0")" && pwd)"
readonly STATE_DIR=/etc/tyj
readonly CURRENT_FILE="$STATE_DIR/app-image"
readonly LOG_FILE=/var/log/tyj/deploy.log
readonly HEALTH_SECONDS=120

if [[ "$IMAGE" != *@sha256:* ]]; then
  echo "イメージは digest（@sha256:...）で指定してください。タグでは配備しません。" >&2
  exit 2
fi

set -a
# shellcheck disable=SC1091
source "$STATE_DIR/deploy.env"
set +a

compose() {
  docker compose --project-directory "$ROOT" -f "$ROOT/compose.yaml" "$@"
}

log_result() {
  mkdir -p "$(dirname "$LOG_FILE")"
  printf '%s\t%s\t%s\t%s\n' "$(date --iso-8601=seconds)" "${SUDO_USER:-$USER}" "$IMAGE" "$1" >> "$LOG_FILE"
}

trap 'log_result "失敗（行 $LINENO）"' ERR

echo "== 1. イメージを取得し、署名を検証します"
docker pull "$IMAGE"
cosign verify --key "$STATE_DIR/cosign.pub" --insecure-ignore-tlog=true "$IMAGE" > /dev/null

echo "== 2. 直近 24 時間以内のバックアップが成功していることを確かめます"
latest=$(compose exec -T db pgbackrest --stanza=tyj --output=json info \
  | jq -r '[.[0].backup[] | select(.error == false) | .timestamp.stop] | max // 0')
if (( $(date +%s) - latest > 24 * 3600 )); then
  echo "直近 24 時間以内に成功したバックアップがありません。配備を中止します。" >&2
  log_result "中止（バックアップなし）"
  exit 1
fi

echo "== 3. マイグレーションを適用します（所有者の接続情報は、この実行のときだけ渡す）"
TYJ_APP_IMAGE="$IMAGE" compose run --rm --no-deps \
  --entrypoint dotnet \
  -v "$STATE_DIR/secrets/db_owner_connection:/run/secrets/db_owner_connection:ro" \
  app /tool/taskyojitsu-tool.dll migrate

echo "== 4. アプリのコンテナを新しいイメージで作り直します"
previous=$(cat "$CURRENT_FILE" 2>/dev/null || true)
TYJ_APP_IMAGE="$IMAGE" compose up -d --no-deps app

echo "== 5. /health/ready を確かめます（最大 ${HEALTH_SECONDS} 秒）"
healthy=false
for _ in $(seq 1 $((HEALTH_SECONDS / 5))); do
  # app はシェルのないイメージのため、nginx のコンテナから確かめる
  if compose exec -T proxy wget -q -O /dev/null -T 4 http://app:8080/health/ready; then
    healthy=true
    break
  fi
  sleep 5
done

if [[ "$healthy" != true ]]; then
  echo "応答がありません。前の版に戻します。" >&2
  if [[ -n "$previous" ]]; then
    # DB の変更は前の版でも動く形にしてあるため（詳細設計書 3.6）、DB は戻さない
    TYJ_APP_IMAGE="$previous" compose up -d --no-deps app
  fi
  log_result "失敗（前の版 ${previous:-なし} に戻した）"
  exit 1
fi

echo "$IMAGE" > "$CURRENT_FILE"
echo "== 6. 結果を記録します"
log_result "成功"
echo "配備が完了しました: $IMAGE"
