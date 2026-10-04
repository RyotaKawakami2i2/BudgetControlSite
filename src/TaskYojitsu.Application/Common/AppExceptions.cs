namespace TaskYojitsu.Application.Common;

/// <summary>
/// 業務処理の失敗。Web 層で RFC 9457 の Problem Details に変える（詳細設計書 5.2）。
/// メッセージは利用者に見せない内部向けのもので、画面にはコードとメッセージ ID を渡す。
/// </summary>
public abstract class AppException(string code, int status, string message) : Exception(message)
{
    /// <summary>エラーのコード（例: resource.not_found）。</summary>
    public string Code { get; } = code;

    /// <summary>HTTP のステータス。</summary>
    public int Status { get; } = status;
}

/// <summary>見つからない（権限がない場合も同じ。NF-ACC-03）。</summary>
public sealed class NotFoundException() : AppException("resource.not_found", 404, "対象が見つかりません。");

/// <summary>権限がない。</summary>
public sealed class ForbiddenException(string code = "auth.forbidden") : AppException(code, 403, "権限がありません。")
{
    public static ForbiddenException Archived() => new("team.archived");

    public static ForbiddenException ReauthRequired() => new("auth.reauth_required");
}

/// <summary>未ログイン。</summary>
public sealed class UnauthenticatedException() : AppException("auth.unauthenticated", 401, "ログインしていません。");

/// <summary>入力の誤り。項目ごとにメッセージ ID を持つ。</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("validation.failed", 400, "入力内容を確認してください。")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationException For(string field, string messageId) =>
        new(new Dictionary<string, string[]> { [field] = [messageId] });
}

/// <summary>ほかの人が先に更新した（同時編集。詳細設計書 4.7）。最新の内容を持つ。</summary>
public sealed class ConflictException(object? latest = null)
    : AppException("concurrency.conflict", 409, "ほかの人が先に更新しました。")
{
    public object? Latest { get; } = latest;
}

/// <summary>業務ルールに反する（最後のリーダーなど）。</summary>
public sealed class RuleViolationException(params string[] messageIds)
    : AppException("rule.violation", 409, "業務ルールに反しています。")
{
    public IReadOnlyList<string> MessageIds { get; } = messageIds;
}
