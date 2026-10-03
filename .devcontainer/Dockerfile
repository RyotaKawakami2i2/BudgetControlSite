# 開発コンテナのイメージ（開発専用）
# .NET 10 SDK と Ubuntu 24.04（本番のアプリのイメージと同じ系統）に、DB を確かめるための psql を加える。
# Claude Code の設定を保存するボリュームの置き場所を先に作り、ボリュームが vscode ユーザーの持ち物になるようにする。
# ベースイメージに入っている Yarn の apt リポジトリは署名鍵が古く apt-get update が失敗するため、先に取り除く（Yarn は使わない）。
FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0-noble

RUN rm -f /etc/apt/sources.list.d/yarn* \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/vscode/.claude \
    && chown vscode:vscode /home/vscode/.claude
