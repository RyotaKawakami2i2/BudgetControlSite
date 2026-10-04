using System.Security.Cryptography;
using System.Text;

namespace TaskYojitsu.Infrastructure.Identity;

/// <summary>HMAC に使う秘密の値（pepper）。秘密情報 recovery_code_pepper、login_hint_pepper から読む（詳細設計書 11章）。</summary>
public sealed class PepperOptions
{
    public string? RecoveryCodePepper { get; set; }
    public string? LoginHintPepper { get; set; }
}

/// <summary>
/// リカバリーコードの HMAC-SHA256（詳細設計書 6.4）。コードは Crockford の Base32 で、入力のゆれ（小文字、ハイフン、I・L・O）を正規化してから計算する。
/// </summary>
public sealed class RecoveryCodeHasher(byte[] pepper)
{
    public string Hash(string code)
    {
        var normalized = Normalize(code);
        return Convert.ToHexString(HMACSHA256.HashData(pepper, Encoding.ASCII.GetBytes(normalized)));
    }

    public static string Normalize(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var c in code.ToUpperInvariant())
        {
            switch (c)
            {
                case '-' or ' ':
                    continue;
                case 'I' or 'L':
                    builder.Append('1');
                    break;
                case 'O':
                    builder.Append('0');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>ログインに失敗したときに入力されたメールアドレスの HMAC（平文は残さない。詳細設計書 8.5）。</summary>
public sealed class LoginHintHasher(byte[] pepper)
{
    public string Hash(string email) =>
        Convert.ToHexString(HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())))[..32];
}

internal static class PepperLoader
{
    /// <summary>秘密の値を読む。開発環境で未設定の場合は、起動ごとの乱数にする（再起動すると古い値は照合できない）。</summary>
    public static byte[] Load(string? value, bool allowRandom, string name)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var bytes = Encoding.UTF8.GetBytes(value.Trim());
            if (bytes.Length < 32)
            {
                throw new InvalidOperationException($"{name} は 32 バイト以上にしてください。");
            }

            return bytes;
        }

        if (!allowRandom)
        {
            throw new InvalidOperationException($"秘密情報 {name} が設定されていません。");
        }

        return RandomNumberGenerator.GetBytes(32);
    }
}
