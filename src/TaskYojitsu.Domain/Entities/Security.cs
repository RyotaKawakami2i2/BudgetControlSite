using System.Net;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Entities;

/// <summary>サーバー側のセッション（user_sessions）。Cookie にはランダムな鍵だけを入れ、ここには鍵のハッシュ値を保存する。</summary>
public class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Cookie に入れた鍵の SHA-256。</summary>
    public byte[] KeyHash { get; set; } = [];

    /// <summary>認証情報（データ保護で暗号化したもの）。</summary>
    public byte[] Ticket { get; set; } = [];

    public AuthMethod AuthMethod { get; set; }

    /// <summary>最後に認証した日時（再認証を含む）。</summary>
    public DateTime AuthTime { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>最後に操作した日時。画面が自動で送る要求では更新しない。</summary>
    public DateTime LastSeenAt { get; set; }

    public DateTime ExpiresAt { get; set; }
    public IPAddress Ip { get; set; } = IPAddress.None;
    public string UserAgent { get; set; } = "";
    public DateTime? RevokedAt { get; set; }
    public SessionRevokeReason? RevokedReason { get; set; }
}

/// <summary>ログインしたことのある端末（user_known_devices）。新しい端末からのログインの検知に使う。</summary>
public class UserKnownDevice
{
    public Guid UserId { get; set; }
    public byte[] Fingerprint { get; set; } = [];
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

/// <summary>招待（invitations）。トークンはハッシュ値だけを保存する。</summary>
public class Invitation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>招待した管理者。</summary>
    public Guid? CreatedBy { get; set; }
}

/// <summary>パスワード再設定のトークン（password_reset_tokens）。</summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>申請した送信元。</summary>
    public IPAddress? RequestIp { get; set; }
}
