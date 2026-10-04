using System.Globalization;

namespace TaskYojitsu.Application.Accounts;

/// <summary>
/// メールの文面（詳細設計書 10章）。すべてテキストで、業務の内容は書かず、システムへのリンクだけを載せる。
/// </summary>
public static class EmailTemplates
{
    private const string Footer = "\n\n--\nタスク予実管理システム\nこのメールには返信できません。\n";

    public static (string Subject, string Body) Invitation(string displayName, string link, int validHours) => (
        "【タスク予実管理】アカウントへの招待",
        $"""
        {displayName} さん

        タスク予実管理システムの管理者から、アカウントへの招待が届いています。
        次のリンクを開き、{validHours} 時間以内に初期設定（パスワードの設定）を行ってください。
        あわせて、認証アプリかパスキーによる多要素認証の設定をおすすめします。
        リンクは 1 回だけ使えます。

        {link}

        心当たりがない場合は、このメールを破棄してください。
        """ + Footer);

    public static (string Subject, string Body) PasswordReset(string link, int validMinutes) => (
        "【タスク予実管理】パスワード再設定のご案内",
        $"""
        パスワードの再設定の申請を受け付けました。
        次のリンクを開き、{validMinutes} 分以内に新しいパスワードを設定してください。

        {link}

        申請した覚えがない場合は、このメールを破棄してください。パスワードは変わりません。
        """ + Footer);

    public static (string Subject, string Body) NewDevice(DateTime localTime, string browser, string ip) => (
        "【タスク予実管理】新しい端末からのログイン",
        $"""
        {localTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)}（日本時間）に、次の端末からあなたのアカウントにログインがありました。

        ブラウザ: {browser}
        IP アドレス: {ip}

        心当たりがない場合は、すぐにパスワードを変更し、管理者に連絡してください。
        アカウント設定の「ログイン中の端末」から、ほかの端末のログインを取り消せます。
        """ + Footer);

    public static (string Subject, string Body) PasswordChanged(DateTime localTime) => (
        "【タスク予実管理】パスワードが変更されました",
        $"""
        {localTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)}（日本時間）に、あなたのアカウントのパスワードが変更されました。
        ほかの端末のログインは、すべて取り消しました。

        心当たりがない場合は、すぐに管理者に連絡してください。
        """ + Footer);

    public static (string Subject, string Body) LoginSettingsChanged(DateTime localTime, string change) => (
        "【タスク予実管理】ログインの設定が変更されました",
        $"""
        {localTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)}（日本時間）に、あなたのアカウントのログインの設定が変更されました。

        変更の内容: {change}

        心当たりがない場合は、すぐにパスワードを変更し、管理者に連絡してください。
        """ + Footer);

    public static (string Subject, string Body) Locked(DateTime localTime, int minutes) => (
        "【タスク予実管理】アカウントを一時的にロックしました",
        $"""
        {localTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)}（日本時間）に、ログインに続けて失敗したため、あなたのアカウントを一時的にロックしました。
        {minutes} 分ほどたつと、再びログインできます。

        心当たりがない場合は、第三者がログインを試みている可能性があります。管理者に連絡してください。
        """ + Footer);

    public static (string Subject, string Body) RecoveryCodeUsed(DateTime localTime, int remaining) => (
        "【タスク予実管理】リカバリーコードでログインしました",
        $"""
        {localTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)}（日本時間）に、リカバリーコードを使ってあなたのアカウントにログインがありました。
        残りのリカバリーコードは {remaining} 個です。

        心当たりがない場合は、すぐにパスワードを変更し、管理者に連絡してください。
        """ + Footer);

    public static (string Subject, string Body) SecurityAlert(string summary, string detail) => (
        $"【タスク予実管理】セキュリティの警告: {summary}",
        $"""
        次の出来事を検知しました。監査ログを確認してください。

        {detail}
        """ + Footer);
}
