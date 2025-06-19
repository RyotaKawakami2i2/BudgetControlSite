# タスク・作業時間管理Webサイト 詳細設計書

## 1. ディレクトリ構成（ASP.NET Core MVC）

- Controllers/
  - HomeController.cs
  - AccountController.cs（ForgotPassword/ResetPasswordアクションを含む）
  - TaskController.cs
  - TeamController.cs
  - UserController.cs
  - ReportController.cs
  - RegisterController.cs
- Models/
  - User.cs（ResetPasswordToken, ResetTokenExpiryカラム追加）
  - Team.cs
  - Job.cs
  - ScheduledTime.cs
  - ActualTime.cs
  - Category.cs
  - Project.cs
  - TimeEntry.cs
  - Notification.cs
  - TeamUser.cs
- Views/
  - Home/Index.cshtml, Privacy.cshtml
  - Account/Login.cshtml, Register.cshtml, ForgotPassword.cshtml, ResetPassword.cshtml
  - Task/Index.cshtml, Edit.cshtml, Detail.cshtml
  - Team/Index.cshtml, Edit.cshtml, MemberJobs.cshtml, CreateMemberJob.cshtml, Members.cshtml
  - User/Index.cshtml, Edit.cshtml
  - Report/Index.cshtml
  - Team/CreateMember.cshtml
- wwwroot/
  - css/site.css
  - js/site.js

## 2. 主要モデル定義

### User.cs
```csharp
public class User {
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public string Role { get; set; } // Admin, Leader, User
    public ICollection<TeamUser> TeamUsers { get; set; }
    public string? ResetPasswordToken { get; set; } // パスワードリセット用
    public DateTime? ResetTokenExpiry { get; set; } // パスワードリセット用
}
```

### Team.cs
```csharp
public class Team {
    public int Id { get; set; }
    public string Name { get; set; }
    public int LeaderId { get; set; }
    public User Leader { get; set; }
    public ICollection<TeamUser> TeamUsers { get; set; }
}
```

### Job.cs
```csharp
public class Job {
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public int AssignedUserId { get; set; }
    public User AssignedUser { get; set; }
    public int? ParentJobId { get; set; }
    public Job ParentJob { get; set; }
    public string Status { get; set; } // 未着手, 進行中, 完了
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public double ScheduledHours { get; set; }
    public double ActualHours { get; set; }
}
```

## 3. コントローラー設計
- HomeController: トップ・ダッシュボード
- AccountController:
  - Login/Logout/Register
  - ForgotPassword（GET/POST）: メールアドレス入力・リセットメール送信
  - ResetPassword（GET/POST）: トークン検証・新パスワード設定
- TaskController: ジョブCRUD、進捗・ガントチャート
- TeamController: チーム管理、メンバー予定入力、メンバー追加/削除
- UserController: ユーザー管理
- ReportController: レポート出力
- RegisterController: アカウント登録（管理者・リーダーのみ）

## 4. 画面設計
- Home/Index: システム概要・機能紹介・ログイン導線
- Account/Login: メール・パスワード入力、パスワードリセット申請リンク
- Account/Register: 新規ユーザー登録（User権限固定）
- Account/ForgotPassword: メールアドレス入力・リセット申請
- Account/ResetPassword: 新パスワード入力
- Task/Index: ジョブ一覧、検索・絞り込み、ページネーション
- Task/Edit: ジョブ登録・編集フォーム
- Team/Index: チーム一覧、検索・絞り込み、ページネーション
- Team/Edit: チーム登録・編集フォーム
- Team/Members: チームメンバー一覧・追加・削除
- Team/MemberJobs: メンバーごとのジョブ一覧、予定入力
- Team/CreateMemberJob: メンバーのジョブ新規登録フォーム
- Team/CreateMember: チームメンバー追加フォーム（管理者・リーダーのみ）
- Report/Index: 期間・条件指定でPDF/Excel出力

## 5. バリデーション・セキュリティ
- 入力値のサーバー/クライアント両方でバリデーション
- パスワードはハッシュ化
- CSRF/XSS/SQLインジェクション対策
- アカウント登録・チームメンバー追加は管理者・リーダーのみアクセス可（サーバー側で権限制御）

## 6. DBテーブル定義（例: PostgreSQL）
- users(id, name, email, password_hash, role, reset_password_token, reset_token_expiry, ...)
- teams(id, name, leader_id, ...)
- team_user(id, team_id, user_id)
- jobs(id, title, description, assigned_user_id, parent_job_id, status, scheduled_start, scheduled_end, actual_start, actual_end, scheduled_hours, actual_hours, ...)
- ...

## 7. 外部連携
- メール送信（SMTP: MailHog, appsettings.Development.jsonで設定）
- ファイル出力: PDF/Excel
- インポート: CSV/Excel

## 8. 非機能要件
- SSL/TLS, パスワードハッシュ, XSS/CSRF/SQLi対策
- レスポンシブデザイン
- バックアップ: 日次 