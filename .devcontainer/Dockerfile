# 開発コンテナのイメージ（開発専用）
# .NET 10 SDK と Ubuntu 24.04（本番のアプリのイメージと同じ系統）に、DB を確かめるための psql を加える。
# Claude Code の設定を保存するボリュームの置き場所を先に作り、ボリュームが vscode ユーザーの持ち物になるようにする。
# ベースイメージに入っている Yarn の apt リポジトリは署名鍵が古く apt-get update が失敗するため、先に取り除く（Yarn は使わない）。
# global.json で .NET 10 の正式版の SDK（10.0.401 以降）を求めるため、正式版の SDK が入った版をダイジェストで固定する
# （2.2.3-10.0-noble: .NET 10.0.12、SDK 10.0.401。2026-09-10 公開）。
FROM mcr.microsoft.com/devcontainers/dotnet:2.2.3-10.0-noble@sha256:9eb314432a5fa67773a53ab754cdc381cae614906162f9fc44cdb7f7837b91c7

RUN rm -f /etc/apt/sources.list.d/yarn* \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /home/vscode/.claude \
    && chown vscode:vscode /home/vscode/.claude
