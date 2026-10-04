using System.Collections.Frozen;
using System.Text.Json;

namespace TaskYojitsu.Domain.Codes;

/// <summary>
/// 区分値とコードの対応をまとめる（DATA-04、DEV-04）。コードは列挙子の名前を snake_case にしたもの（例: NotStarted → not_started）。
/// API の JSON も同じ規則（JsonStringEnumConverter と SnakeCaseLower）で変換するため、対応はここだけで決まる。
/// </summary>
public static class EnumCodes
{
    public static string ToCode<T>(this T value) where T : struct, Enum => Cache<T>.ToCode[value];

    public static T Parse<T>(string code) where T : struct, Enum =>
        Cache<T>.FromCode.TryGetValue(code, out var value)
            ? value
            : throw new FormatException($"'{code}' は {typeof(T).Name} のコードではありません。");

    public static bool TryParse<T>(string? code, out T value) where T : struct, Enum
    {
        if (code is not null && Cache<T>.FromCode.TryGetValue(code, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>コードの一覧（DB の CHECK 制約やテストで使う）。</summary>
    public static IReadOnlyList<string> AllCodes<T>() where T : struct, Enum => Cache<T>.Codes;

    private static class Cache<T> where T : struct, Enum
    {
        public static readonly FrozenDictionary<T, string> ToCode =
            Enum.GetValues<T>().ToFrozenDictionary(v => v, v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()));

        public static readonly FrozenDictionary<string, T> FromCode =
            ToCode.ToFrozenDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

        public static readonly IReadOnlyList<string> Codes = [.. ToCode.Values.Order(StringComparer.Ordinal)];
    }
}
