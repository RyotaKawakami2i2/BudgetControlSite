#!/usr/bin/env bash
# 開発コンテナを作った直後に 1 回だけ動く。
# まだ無いもの（プロジェクトを作る前の .NET のソリューションや web/ など）は飛ばす。
set -euo pipefail
cd "$(dirname "$0")/.."

echo "== ツールの版"
dotnet --version
node --version
pnpm --version
psql --version

# .NET のローカルツール（dotnet-ef など）
if [ -f .config/dotnet-tools.json ]; then
  dotnet tool restore
fi

# .NET の依存パッケージ
if compgen -G "*.sln" >/dev/null || compgen -G "*.slnx" >/dev/null; then
  dotnet restore
fi

# 画面側の依存パッケージ。ロックファイルどおりに入れる（インストール時のスクリプトは pnpm の既定で動かない）
if [ -f web/package.json ] && [ -f web/pnpm-lock.yaml ]; then
  (cd web && pnpm install --frozen-lockfile)
fi

echo "== 準備ができました"
