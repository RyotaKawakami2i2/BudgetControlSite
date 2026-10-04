using Microsoft.AspNetCore.Identity;
using TaskYojitsu.Domain.Codes;

namespace TaskYojitsu.Domain.Entities;

/// <summary>利用者（users）。Identity のユーザー情報を含む。利用者は削除せず無効化する。</summary>
public class User : IdentityUser<Guid>
{
    /// <summary>表示名。表示にだけ使い、識別には Id を使う。</summary>
    public string DisplayName { get; set; } = "";

    public UserStatus Status { get; set; } = UserStatus.Invited;

    public bool IsAdmin { get; set; }

    /// <summary>最初に開くビュー。</summary>
    public Guid? DefaultViewId { get; set; }

    /// <summary>最後に受け付けた認証コードの時間区分（同じコードの再利用を防ぐ）。</summary>
    public long? LastTotpStep { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime? DisabledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
