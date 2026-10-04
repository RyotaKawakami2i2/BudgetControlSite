# BudgetControlSite（タスク予実管理システム）

チームのタスクの予定と実績（日程・工数・進捗）をガントチャートで管理する、社内向けの Web アプリです。
中身の保証はできません。仕様は [要件定義書](docs/requirements_v2.md)、[基本設計書](docs/basic_design_v2.md)、[詳細設計書](docs/detail_design_v2.md) にあります。

![ガントの画面](docs/images/gantt.png)

## 目指していること

チームのタスクをガントチャートで計画して割り振り、メンバーが入力した実績（日程・工数）を予定と比べて、**遅れを早く見つけられる**ようにすることが目的です。

- チームの「誰が・いつ・何を」を、ガントチャートで一目で把握できる
- リーダーがタスクを割り振り、メンバーの負荷と進み具合を確かめられる
- メンバーが、自分のタスクの追加と実績の入力を手早くできる（1 件 30 秒程度）
- 予定と実績の差（遅れ・超過）がガントで強調され、それだけに絞り込める
- 一般的な水準を上回るセキュリティ（OWASP ASVS 5.0 のレベル 2 相当）を確保する

予算や原価などの金額は扱わず、予実は日程と工数で比べます。プロジェクトごとにチームを作り、社内のサーバーで動かします。

## できること

| 立場 | できること |
| --- | --- |
| メンバー | 担当タスクを期限ごとに確かめる（マイタスク）。作業実績・進捗・完了を記録する。週の入力表で 1 週間分をまとめて入力する。自分のタスクを追加する。予定の変更はコメントで依頼する |
| リーダー | チーム（プロジェクト）を作り、メンバーを加える。ガントでタスクと子タスクを計画し、担当者を割り振る。バーをドラッグして日程を変える。担当者別の予実を確かめる。タグや共有のビューを管理する |
| 管理者 | 利用者を招待・無効化する。リーダーを指定してチームを作る。すべてのチームを閲覧する（変更はできない）。監査ログを検索する。祝日を登録する |

ガントは担当者別・チーム別・状態別にまとめたり、「自分のタスク」「遅れ」などで絞り込んだりでき、その表示を URL で共有したりビューとして保存したりできます。タスクの担当になったときや期限が近いときは、画面の通知で知らせます。

## システムの構成

```mermaid
flowchart LR
  subgraph browser["ブラウザー"]
    spa["業務の画面<br/>React SPA（/app）"]
    authui["認証の画面<br/>（/account）"]
  end
  subgraph app["アプリ（ASP.NET Core / .NET 10）"]
    web["Web<br/>API・認証の画面"]
    application["Application<br/>権限の判定・業務の処理"]
    domain["Domain<br/>業務ルール"]
    infra["Infrastructure<br/>DB・セッション<br/>監査ログ・メール"]
  end
  db[("PostgreSQL 18")]
  mail["メールサーバー<br/>（開発では Mailpit）"]
  spa -- "JSON の API（/api/v1）" --> web
  authui -- "フォームの送信" --> web
  web --> application --> domain
  application --> infra
  infra --> db
  infra --> mail
```

- **フロントエンド**: 業務の画面は React と TypeScript で作り、Vite でビルドします。ビルドしたファイルはアプリと同じサーバーから同じ URL の下（`/app`）で配信するので、画面用のサーバーは別に要りません。データは JSON の API で読み書きし、集計や遅れの判定はサーバーだけで行います。
- **認証の画面**: ログイン、パスワードの再設定、多要素認証の設定などは、サーバーで HTML を作る Blazor の静的 SSR です。
- **バックエンド**: 上の図の 4 つの層に分けています。共通の確認（セッション、CSRF、回数制限、セキュリティのヘッダー）は Web 層のミドルウェアで、権限の判定は Application 層の AccessPolicy の 1 か所だけで行います。
- **本番**: ブラウザーとアプリの間に nginx（HTTPS）を置き、Docker で動かします（[deploy/](deploy/)）。

| フォルダー | 内容 |
| --- | --- |
| `src/` | サーバー（`TaskYojitsu.Domain`、`.Application`、`.Infrastructure`、`.Web`）と運用コマンド（`.Tool`） |
| `web/` | 業務の画面（React）。ビルドすると `src/TaskYojitsu.Web/wwwroot/app` に出力する |
| `tests/` | 単体テスト、API と権限の結合テスト、通しのテスト（Playwright） |
| `deploy/` | 本番の構成（Dockerfile、compose、nginx、PostgreSQL）と配備スクリプト |
| `scripts/` | 開発用のスクリプト（DB の作り直し） |
| `.devcontainer/`、`.github/` | 開発コンテナ、CI（GitHub Actions） |

## セキュリティ

API への 1 回の要求は、次の順に確かめてから処理します。

```mermaid
flowchart LR
  r["API の要求"] --> s1["回数制限<br/>利用者・送信元ごと"]
  s1 --> s2["セッションの確認<br/>DB の記録と照合"]
  s2 --> s3["CSRF の確認<br/>Origin・トークン・JSON"]
  s3 --> s4["権限の判定<br/>AccessPolicy"]
  s4 --> s5["入力の検査と処理<br/>変更は監査ログに記録"]
```

| 観点 | 対策 |
| --- | --- |
| ログイン | パスワードは 15 文字以上で、よく使われるものは使えない。PBKDF2（30 万回）で保存する。5 回続けて失敗すると 15 分ロックし、本人にメールで知らせる。多要素認証（認証アプリ・パスキー）に対応 |
| セッション | Cookie にはランダムな ID だけを入れ、中身は DB に保存する（`__Host-`、HttpOnly、Secure、SameSite）。30 分操作がないか 12 時間たつと切れる。ログアウトやアカウントの無効化はすぐに反映される |
| CSRF | 状態を変える要求では、Origin、トークン（`X-XSRF-TOKEN`）、JSON であることの 3 つを確かめる |
| 権限 | 判定は詳細設計書 4.5 の表のとおり。所属していないチームのデータは 404 にして、あること自体を見せない。管理の操作には直近 10 分以内の再認証が要る |
| 画面 | CSP で、自サイト以外のスクリプトと HTML の中のスクリプトを禁止する。HSTS や X-Frame-Options などのヘッダーを付け、ログイン後の応答はキャッシュさせない |
| 監査ログ | ログイン、データの変更、権限による拒否を記録する。ハッシュの鎖で改ざんを見つけられ、アプリの DB アカウントは追記しかできない |
| DB と秘密情報 | アプリは権限を絞った DB アカウント（`tyj_app`）で接続する。秘密情報は設定ファイルに書かず、本番では Docker の secrets で渡す |
| 依存パッケージ | 版を固定し、公開から 7 日以上たった版だけを使う。CI で CodeQL、脆弱性の検査、イメージの検査を行う |

## 初回の準備と起動

必要なもの: Docker（Docker Desktop など）と、VS Code の拡張機能「Dev Containers」

1. リポジトリを VS Code で開き、「コンテナーで再度開く」を選びます。
   開発用のコンテナ、PostgreSQL 18、Mailpit が起動します。DB のアカウントと DB は初回に自動で作られ、依存パッケージも自動で入ります。
2. DB のテーブルを作り、デモデータを入れます。パスワードは 15 文字以上で決めてください（デモの利用者の全員に同じものを設定します）。

   ```bash
   bash scripts/dev-reset-db.sh --seed blue-river-under-cloudy-sky
   ```

   途中で `Failed executing DbCommand ... __ef_migrations_history` と表示されますが、DB を作り直した直後の確認で出るもので、問題ありません。

3. 画面をビルドし、アプリを起動します。コマンドは、すべてリポジトリの直下（`/workspaces/BudgetControlSite`）で実行します。

   ```bash
   cd web
   pnpm build
   cd ..
   dotnet run --project src/TaskYojitsu.Web
   ```

4. <http://localhost:5080> を開き、手順 2 のパスワードでログインします。

| メールアドレス | 立場 |
| --- | --- |
| `admin@example.com` | 管理者（チームには所属していないので、チームのデータは閲覧だけ） |
| `yamada@example.com` | 「販売管理更改」「社内ポータル改善」のリーダー |
| `suzuki@example.com`、`sato@example.com` | 「販売管理更改」のメンバー |
| `tanaka@example.com` | 「社内ポータル改善」のメンバー |

- 招待やロックのお知らせなど、送ったメールは Mailpit（<http://localhost:8025>）で確かめます。
- データを最初の状態に戻すときは、手順 2 をもう一度実行します。
- 画面を直しながら確かめるときは、アプリを起動したまま `cd web && pnpm dev` を実行し、<http://localhost:5173/app/> を開きます。
- `dotnet` が SDK のエラーを出すときは、コンテナーをリビルドしてください（.NET 10 の正式版の SDK が必要です）。

## テスト

```bash
# 単体テストと結合テスト（結合テストは実際の PostgreSQL を使う）
dotnet test --solution TaskYojitsu.slnx

# 画面の静的検査と単体テスト
(cd web && pnpm lint && pnpm typecheck && pnpm test)

# 通しのテスト（Playwright）。全員が同じ送信元から操作するため、送信元ごとの回数制限を広げてアプリを起動しておく
RateLimits__LoginByIp__Permits=100 RateLimits__ApiByIp__Permits=100000 dotnet run --project src/TaskYojitsu.Web
(cd tests/e2e && pnpm exec playwright install --with-deps chromium && E2E_PASSWORD=<手順 2 のパスワード> pnpm test)
```

プルリクエストを作ったときと、そのブランチに変更をプッシュしたときは、GitHub Actions がすべてのテストを動かします。

## 本番と運用

- イメージを `docker build -f deploy/Dockerfile .` で作り、`deploy/deploy.sh <イメージ@sha256:...>` で配備します（署名の検証、マイグレーション、失敗したときの切り戻しを含む）。
- 運用コマンドの一覧は `dotnet run --project src/TaskYojitsu.Tool -- help` で表示します（最初の管理者の作成、祝日の取り込み、監査ログの検証など）。
- 設定値と秘密情報は詳細設計書の 11 章、配備は 12 章を参照してください。
