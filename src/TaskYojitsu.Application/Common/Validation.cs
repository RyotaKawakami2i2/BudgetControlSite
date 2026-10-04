using System.Diagnostics.CodeAnalysis;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Common;

/// <summary>入力の検査の結果を集める（NF-INP-01、詳細設計書 4.6）。</summary>
public sealed class Validation
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string messageId)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        if (!list.Contains(messageId))
        {
            list.Add(messageId);
        }
    }

    public bool HasError(string field) => _errors.ContainsKey(field);

    public void ThrowIfAny()
    {
        if (HasErrors)
        {
            throw new ValidationException(_errors.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        }
    }

    /// <summary>
    /// 1 行の項目を正規化して検査する。必須なら空を誤りにする。正規化した値を返す。
    /// </summary>
    public string? SingleLine(string field, string? value, int max, bool required, string? requiredMessage = null, string? tooLongMessage = null)
    {
        if (value is not null && TextRules.HasForbiddenControlChars(value))
        {
            Add(field, Msg.CmnChoice);
            return null;
        }

        var normalized = TextRules.NormalizeSingleLine(value);
        if (normalized is null)
        {
            if (required)
            {
                Add(field, requiredMessage ?? Msg.CmnRequired);
            }

            return null;
        }

        if (TextRules.Length(normalized) > max)
        {
            Add(field, tooLongMessage ?? Msg.CmnTooLong);
        }

        return normalized;
    }

    /// <summary>複数行の項目（説明、コメントなど）を正規化して検査する。</summary>
    public string? Multiline(string field, string? value, int max, bool required = false)
    {
        if (value is not null && TextRules.HasForbiddenControlChars(value))
        {
            Add(field, Msg.CmnChoice);
            return null;
        }

        var normalized = TextRules.NormalizeMultiline(value);
        if (normalized is null)
        {
            if (required)
            {
                Add(field, Msg.CmnRequired);
            }

            return null;
        }

        if (TextRules.Length(normalized) > max)
        {
            Add(field, Msg.CmnTooLong);
        }

        return normalized;
    }

    /// <summary>区分値のコードを検査する。</summary>
    public bool TryCode<T>(string field, string? code, [NotNullWhen(true)] out T? value) where T : struct, Enum
    {
        if (Domain.Codes.EnumCodes.TryParse<T>(code, out var parsed))
        {
            value = parsed;
            return true;
        }

        Add(field, Msg.CmnChoice);
        value = null;
        return false;
    }
}
