using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Infrastructure.Identity;

/// <summary>
/// パスワードの保存（詳細設計書 6.2 の手順 1 と 7）。入力を Unicode の NFKC に正規化してから、
/// PBKDF2-HMAC-SHA512（300,000 回）で保存する。古い回数で保存されたものは、ログインに成功したときに計算し直される。
/// </summary>
public sealed class NormalizingPasswordHasher(IOptions<PasswordHasherOptions> options) : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _inner = new(options);

    public static string Normalize(string password) => password.Normalize(NormalizationForm.FormKC);

    public string HashPassword(User user, string password) => _inner.HashPassword(user, Normalize(password));

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword) =>
        _inner.VerifyHashedPassword(user, hashedPassword, Normalize(providedPassword));
}

/// <summary>同梱した頻出パスワードの一覧（約 10 万件。大文字と小文字は区別しない）。</summary>
public sealed class CommonPasswordList
{
    private readonly HashSet<string> _passwords;

    public CommonPasswordList()
    {
        using var stream = typeof(CommonPasswordList).Assembly.GetManifestResourceStream("TaskYojitsu.CommonPasswords")
            ?? throw new InvalidOperationException("頻出パスワードの一覧が見つかりません。");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        _passwords = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                _passwords.Add(line);
            }
        }
    }

    public int Count => _passwords.Count;

    public bool Contains(string normalizedPassword) =>
        _passwords.Contains(normalizedPassword.ToLowerInvariant());
}

/// <summary>
/// パスワードの検査（NF-AUT-02、詳細設計書 6.2）。文字の種類の組み合わせは強制せず、長さ、頻出・漏えい、個人の情報、単純な並びを確かめる。
/// エラーのコードには、画面に出すメッセージ ID を入れる。
/// </summary>
public sealed class PasswordPolicyValidator(
    CommonPasswordList commonPasswords,
    IOptions<SecurityOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<PasswordPolicyValidator> logger) : IPasswordValidator<User>
{
    private static readonly string[] Sequences =
    [
        "abcdefghijklmnopqrstuvwxyz",
        "0123456789",
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
        "qwertyuiopasdfghjklzxcvbnm",
        "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik9ol0p",
        "1q2w3e4r5t6y7u8i9o0p",
    ];

    public static IdentityError Error(string messageId) => new() { Code = messageId, Description = messageId };

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        var settings = options.Value.Password;
        var normalized = NormalizingPasswordHasher.Normalize(password ?? "");
        var length = TextRules.Length(normalized);
        if (length < settings.MinLength || length > settings.MaxLength)
        {
            return IdentityResult.Failed(Error("MSG-AUT-003"));
        }

        var lower = normalized.ToLowerInvariant();
        if (commonPasswords.Contains(lower))
        {
            return IdentityResult.Failed(Error("MSG-AUT-004"));
        }

        if (ContainsPersonalInfo(lower, user))
        {
            return IdentityResult.Failed(Error("MSG-AUT-005"));
        }

        if (IsTrivial(lower))
        {
            return IdentityResult.Failed(Error("MSG-AUT-006"));
        }

        if (settings.PwnedApi.Enabled && await IsPwnedAsync(normalized))
        {
            return IdentityResult.Failed(Error("MSG-AUT-004"));
        }

        return IdentityResult.Success;
    }

    /// <summary>メールアドレスの @ より前の部分と、表示名（4 文字以上の場合）を含むか。</summary>
    internal static bool ContainsPersonalInfo(string lowerPassword, User user)
    {
        var local = user.Email?.Split('@')[0].ToLowerInvariant();
        if (!string.IsNullOrEmpty(local) && local.Length >= 3 && lowerPassword.Contains(local, StringComparison.Ordinal))
        {
            return true;
        }

        var name = NormalizingPasswordHasher.Normalize(user.DisplayName).ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        return TextRules.Length(name) >= 4 && lowerPassword.Replace(" ", "", StringComparison.Ordinal).Contains(name, StringComparison.Ordinal);
    }

    /// <summary>同じ文字の繰り返しや続いた文字だけでできているか。使っている文字の種類が 5 つ未満か。</summary>
    internal static bool IsTrivial(string lowerPassword)
    {
        var runes = lowerPassword.EnumerateRunes().Select(r => r.Value).ToArray();
        if (runes.Distinct().Count() < 5)
        {
            return true;
        }

        // 隣り合う文字がすべて「同じ」か「1 つ違い」なら、単純な並び（aaaa、abcd、9876 など）
        var simple = true;
        for (var i = 1; i < runes.Length && simple; i++)
        {
            simple = Math.Abs(runes[i] - runes[i - 1]) <= 1;
        }

        if (simple)
        {
            return true;
        }

        // キーボードや数字の並びの繰り返しだけでできているか
        foreach (var sequence in Sequences)
        {
            if (IsMadeOfSequence(lowerPassword, sequence) || IsMadeOfSequence(lowerPassword, new string(sequence.Reverse().ToArray())))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMadeOfSequence(string password, string sequence)
    {
        // パスワードを、並びの一部分（3 文字以上）の連続に分けられるか
        var position = 0;
        while (position < password.Length)
        {
            var best = 0;
            for (var start = 0; start < sequence.Length; start++)
            {
                var length = 0;
                while (position + length < password.Length && start + length < sequence.Length
                       && password[position + length] == sequence[start + length])
                {
                    length++;
                }

                best = Math.Max(best, length);
            }

            if (best < 3)
            {
                return false;
            }

            position += best;
        }

        return true;
    }

    /// <summary>
    /// 外部の漏えい照合サービスに、SHA-1 の値の先頭 5 文字だけを送って照合する（k-匿名性。既定は無効）。
    /// 照合できない場合は、通す（一覧での検査は済んでいる）。
    /// </summary>
    private async Task<bool> IsPwnedAsync(string password)
    {
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            var client = httpClientFactory.CreateClient("pwned");
            var body = await client.GetStringAsync($"range/{hash[..5]}");
            var suffix = hash[5..];
            return body.Split('\n').Any(line => line.StartsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "漏えいパスワードの照合サービスに接続できませんでした");
            return false;
        }
    }
}
