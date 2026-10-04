using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Entities;

namespace TaskYojitsu.Infrastructure.Identity;

/// <summary>
/// 認証アプリのコードの検証（詳細設計書 6.3）。RFC 6238（HMAC-SHA1、6 桁、30 秒）で、前後 1 区間のずれまで受け付ける。
/// 受け付けたコードの時間区分を users.last_totp_step に保存し、それ以前の区分のコードは拒否する（同じコードの再利用を防ぐ）。
/// </summary>
public sealed class OneTimeTotpProvider(TimeProvider time, IOptions<SecurityOptions> options) : IUserTwoFactorTokenProvider<User>
{
    public const string ProviderName = "TotpOneTime";
    private const int StepSeconds = 30;

    public Task<string> GenerateAsync(string purpose, UserManager<User> manager, User user) =>
        Task.FromResult(string.Empty);

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<User> manager, User user)
    {
        if (token is not { Length: 6 } || !token.All(char.IsAsciiDigit))
        {
            return false;
        }

        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var keyBytes = Base32.Decode(key);
        var code = int.Parse(token, CultureInfo.InvariantCulture);
        var currentStep = time.GetUtcNow().ToUnixTimeSeconds() / StepSeconds;
        var skew = Math.Clamp(options.Value.Totp.AllowedSkewSteps, 0, 2);
        long? matched = null;
        for (var offset = -skew; offset <= skew; offset++)
        {
            var step = currentStep + offset;
            if (Compute(keyBytes, step) == code)
            {
                matched = step;
            }
        }

        if (matched is not { } matchedStep || (user.LastTotpStep is { } last && matchedStep <= last))
        {
            return false;
        }

        user.LastTotpStep = matchedStep;
        var result = await manager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<User> manager, User user) =>
        !string.IsNullOrEmpty(await manager.GetAuthenticatorKeyAsync(user));

    /// <summary>RFC 6238 のコード（テストでも使う）。</summary>
    public static int Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return binary % 1_000_000;
    }
}

/// <summary>RFC 4648 の Base32（認証アプリの鍵の形式）。</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string input)
    {
        var text = input.Trim().TrimEnd('=').Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var output = new byte[text.Length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Base32 の文字ではありません。");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output[index++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
        }

        return output;
    }
}
