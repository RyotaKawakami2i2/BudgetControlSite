using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Infrastructure.Identity;

/// <summary>
/// 利用者の管理。リカバリーコードを Crockford の Base32 の 12 文字（4 文字ずつハイフンで区切る。約 60 ビット）で作る（詳細設計書 6.4）。
/// </summary>
public sealed class AppUserManager(
    IUserStore<User> store,
    IOptions<IdentityOptions> optionsAccessor,
    IPasswordHasher<User> passwordHasher,
    IEnumerable<IUserValidator<User>> userValidators,
    IEnumerable<IPasswordValidator<User>> passwordValidators,
    ILookupNormalizer keyNormalizer,
    IdentityErrorDescriber errors,
    IServiceProvider services,
    ILogger<UserManager<User>> logger)
    : UserManager<User>(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
{
    private const string CrockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    protected override string CreateTwoFactorRecoveryCode()
    {
        Span<char> chars = stackalloc char[14];
        var position = 0;
        for (var i = 0; i < 12; i++)
        {
            if (i is 4 or 8)
            {
                chars[position++] = '-';
            }

            chars[position++] = CrockfordAlphabet[RandomNumberGenerator.GetInt32(CrockfordAlphabet.Length)];
        }

        return new string(chars);
    }
}
