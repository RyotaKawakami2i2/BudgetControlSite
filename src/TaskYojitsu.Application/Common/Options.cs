namespace TaskYojitsu.Application.Common;

// 設定値（詳細設計書 11章）。appsettings.json の同じ名前の節から読む。秘密情報は含めない。

/// <summary>App 節。</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>自サイトの URL。Origin の確認とメールのリンクに使う。</summary>
    public string BaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>業務上の「今日」を決めるタイムゾーン。</summary>
    public string TimeZone { get; set; } = "Asia/Tokyo";
}

/// <summary>Security 節。</summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    public SessionSettings Session { get; set; } = new();
    public ReauthSettings Reauth { get; set; } = new();
    public LockoutSettings Lockout { get; set; } = new();
    public PasswordSettings Password { get; set; } = new();
    public TotpSettings Totp { get; set; } = new();
    public MfaSettings Mfa { get; set; } = new();
    public InvitationSettings Invitation { get; set; } = new();
    public PasswordResetSettings PasswordReset { get; set; } = new();

    /// <summary>管理 API を許可するネットワーク（CIDR）。空なら制限しない。</summary>
    public List<string> AdminAllowedNetworks { get; set; } = [];

    /// <summary>X-Forwarded-For を信用する送信元（nginx）。</summary>
    public List<string> KnownProxies { get; set; } = [];

    public int NewDeviceLookbackDays { get; set; } = 90;

    public sealed class SessionSettings
    {
        public int IdleMinutes { get; set; } = 30;
        public int AbsoluteHours { get; set; } = 12;
        public int WarningMinutes { get; set; } = 5;
        public int TouchIntervalSeconds { get; set; } = 60;
    }

    public sealed class ReauthSettings
    {
        public int Minutes { get; set; } = 10;
    }

    public sealed class LockoutSettings
    {
        public int MaxFailedAttempts { get; set; } = 5;
        public int Minutes { get; set; } = 15;
    }

    public sealed class PasswordSettings
    {
        public int MinLength { get; set; } = 15;
        public int MaxLength { get; set; } = 128;
        public int Pbkdf2Iterations { get; set; } = 300_000;
        public PwnedApiSettings PwnedApi { get; set; } = new();
    }

    public sealed class PwnedApiSettings
    {
        public bool Enabled { get; set; }
        public string? ProxyUrl { get; set; }
    }

    public sealed class TotpSettings
    {
        public int AllowedSkewSteps { get; set; } = 1;
    }

    public sealed class MfaSettings
    {
        /// <summary>true にすると、管理者だけ多要素認証を必須にする（初期値は false）。</summary>
        public bool RequiredForAdmins { get; set; }
    }

    public sealed class InvitationSettings
    {
        public int ValidHours { get; set; } = 72;
    }

    public sealed class PasswordResetSettings
    {
        public int ValidMinutes { get; set; } = 30;
    }
}

/// <summary>業務の設定（Gantt、Tasks、Notifications、Delay、Audit 節）。</summary>
public sealed class BusinessOptions
{
    /// <summary>ガントの 1 回の応答で返すタスクの上限。</summary>
    public int GanttMaxTasksPerResponse { get; set; } = 5000;

    /// <summary>論理削除したタスクを完全に削除するまでの日数。</summary>
    public int SoftDeleteRetentionDays { get; set; } = 365;

    /// <summary>削除したタスクを復元できる日数。</summary>
    public int RestoreWindowDays { get; set; } = 30;

    /// <summary>通知を残す日数。</summary>
    public int NotificationRetentionDays { get; set; } = 180;

    /// <summary>進捗遅れのしきい値（ポイント）。</summary>
    public int ProgressLagThreshold { get; set; } = 20;

    /// <summary>監査ログを残す年数。</summary>
    public int AuditRetentionYears { get; set; } = 3;
}
