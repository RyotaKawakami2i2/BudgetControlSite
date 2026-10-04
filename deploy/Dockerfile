# syntax=docker/dockerfile:1
# 本番のアプリのイメージ（詳細設計書 12.1、12.5 の段階 5）。リポジトリの直下をコンテキストにしてビルドする:
#   docker build -f deploy/Dockerfile -t taskyojitsu .
# ベースイメージは版とダイジェストで固定する（公開から 7 日以上たったもの。基本設計書 8.6）。
# 実行用のイメージはシェルのない chiseled（root 以外の利用者 app で動く）。運用コマンドも同じイメージに入れる（/tool）。

# ---------------------------------------------------------------- 画面（React）のビルド
FROM node:24.21.0-trixie-slim@sha256:8ec5d7557396cfe32d21c3f9c13072355ceab22b584578ca4bb28af31120cffe AS web
WORKDIR /src/web
RUN npm install --global --ignore-scripts pnpm@12.7.0
COPY web/package.json web/pnpm-lock.yaml web/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY web/ ./
# 出力先は ../src/TaskYojitsu.Web/wwwroot/app（vite.config.ts）
RUN pnpm build

# ---------------------------------------------------------------- サーバー（.NET）のビルド
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/TaskYojitsu.Domain/TaskYojitsu.Domain.csproj src/TaskYojitsu.Domain/packages.lock.json src/TaskYojitsu.Domain/
COPY src/TaskYojitsu.Application/TaskYojitsu.Application.csproj src/TaskYojitsu.Application/packages.lock.json src/TaskYojitsu.Application/
COPY src/TaskYojitsu.Infrastructure/TaskYojitsu.Infrastructure.csproj src/TaskYojitsu.Infrastructure/packages.lock.json src/TaskYojitsu.Infrastructure/
COPY src/TaskYojitsu.Web/TaskYojitsu.Web.csproj src/TaskYojitsu.Web/packages.lock.json src/TaskYojitsu.Web/
COPY src/TaskYojitsu.Tool/TaskYojitsu.Tool.csproj src/TaskYojitsu.Tool/packages.lock.json src/TaskYojitsu.Tool/
# ロックファイルと食い違う場合は失敗させる（詳細設計書 12.6）
RUN dotnet restore src/TaskYojitsu.Web/TaskYojitsu.Web.csproj --locked-mode \
    && dotnet restore src/TaskYojitsu.Tool/TaskYojitsu.Tool.csproj --locked-mode
COPY src/ src/
COPY --from=web /src/src/TaskYojitsu.Web/wwwroot/app src/TaskYojitsu.Web/wwwroot/app
RUN dotnet publish src/TaskYojitsu.Web/TaskYojitsu.Web.csproj -c Release --no-restore -o /out/app \
    && dotnet publish src/TaskYojitsu.Tool/TaskYojitsu.Tool.csproj -c Release --no-restore -o /out/tool

# ---------------------------------------------------------------- 実行用
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled@sha256:48e51f2f6798897be7ac4e775c049ed8fe60d3190f637e1f9c9dc7513efa659c
WORKDIR /app
COPY --from=build /out/app ./
COPY --from=build /out/tool /tool/
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TaskYojitsu.Web.dll"]
