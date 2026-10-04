using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace TaskYojitsu.Application.Common;

/// <summary>
/// 招待・再設定・セッションの鍵（256 ビットの乱数）と、そのハッシュ値（SHA-256）。DB にはハッシュ値だけを保存する。
/// </summary>
public static class SecureTokens
{
    /// <summary>新しいトークン（Base64URL）。</summary>
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>トークンのハッシュ値。</summary>
    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>トークンの形式として正しいか（長さと文字の種類だけを確かめる）。</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
