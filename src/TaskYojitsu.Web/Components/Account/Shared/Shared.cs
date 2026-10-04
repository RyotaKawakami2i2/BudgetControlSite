namespace TaskYojitsu.Web.Components.Account.Shared;

/// <summary>パスキーの操作の種類。</summary>
public enum PasskeyOperation
{
    Create = 0,
    Request = 1,
}

/// <summary>ブラウザから受け取るパスキーの情報。</summary>
public sealed class PasskeyInputModel
{
    public string? CredentialJson { get; set; }
    public string? Error { get; set; }
}
