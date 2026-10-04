using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Infrastructure.Identity;

/// <summary>
/// 利用者の保存（詳細設計書 2.2）。認証アプリの鍵はデータ保護で暗号化し、リカバリーコードは HMAC の値だけを保存する。
/// </summary>
public sealed class ProtectedUserStore(
    AppDbContext context,
    IDataProtectionProvider dataProtection,
    RecoveryCodeHasher codeHasher,
    IdentityErrorDescriber? describer = null)
    : UserOnlyStore<User, AppDbContext, Guid, IdentityUserClaim<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>, IdentityUserPasskey<Guid>>(context, describer)
{
    public const string InternalLoginProvider = "[AspNetUserStore]";
    public const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    public const string RecoveryCodeTokenName = "RecoveryCodes";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("TaskYojitsu.AuthenticatorKey.v1");

    public override Task SetAuthenticatorKeyAsync(User user, string key, CancellationToken cancellationToken) =>
        SetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, _protector.Protect(key), cancellationToken);

    public override async Task<string?> GetAuthenticatorKeyAsync(User user, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, cancellationToken);
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(stored);
        }
        catch (CryptographicException)
        {
            // 鍵を復号できない（データ保護の鍵を失った）場合は、未登録として扱う
            return null;
        }
    }

    public override Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName,
            string.Join(";", recoveryCodes.Select(codeHasher.Hash)), cancellationToken);

    public override async Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, cancellationToken) ?? "";
        var hashes = stored.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        var target = Encoding.ASCII.GetBytes(codeHasher.Hash(code));
        var index = hashes.FindIndex(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), target));
        if (index < 0)
        {
            return false;
        }

        // 使ったコードは削除する（1 回限り有効。NF-AUT-09）
        hashes.RemoveAt(index);
        await SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, string.Join(";", hashes), cancellationToken);
        return true;
    }

    public override async Task<int> CountCodesAsync(User user, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, cancellationToken) ?? "";
        return stored.Split(';', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
