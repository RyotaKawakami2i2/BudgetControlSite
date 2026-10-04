using System.Text;

namespace TaskYojitsu.Domain.Rules;

/// <summary>
/// 文字列の正規化と検査（詳細設計書 3.1、4.6）。保存の前に Unicode の NFC に正規化し、前後の空白を除く。
/// 改行とタブ以外の制御文字は受け付けない。長さはコードポイントの数で数える（DB の varchar と同じ）。
/// </summary>
public static class TextRules
{
    /// <summary>複数行の項目を正規化する。空になったら null。</summary>
    public static string? NormalizeMultiline(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        normalized = normalized.Normalize(NormalizationForm.FormC).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>1 行の項目を正規化する（改行とタブは空白にする）。空になったら null。</summary>
    public static string? NormalizeSingleLine(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is '\r' or '\n' or '\t' ? ' ' : c);
        }

        var normalized = builder.ToString().Normalize(NormalizationForm.FormC).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>改行・タブ以外の制御文字を含むか。</summary>
    public static bool HasForbiddenControlChars(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune) && rune.Value is not ('\n' or '\t'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>長さ（コードポイントの数）。</summary>
    public static int Length(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
