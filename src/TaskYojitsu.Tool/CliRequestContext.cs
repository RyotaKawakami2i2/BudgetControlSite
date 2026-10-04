using System.Net;
using TaskYojitsu.Application.Abstractions;

namespace TaskYojitsu.Tool;

/// <summary>運用コマンドでの要求の情報。操作者は、コマンドが必要に応じて設定する（通常は空）。</summary>
public sealed class CliRequestContext : IRequestContext
{
    public Guid? UserId { get; set; }

    public Guid? SessionId => null;

    public DateTime? AuthTime { get; set; }

    public IPAddress? Ip => null;

    public string? UserAgent => "taskyojitsu-tool";

    public string? RequestId { get; } = $"cli-{Guid.NewGuid():N}";

    public bool IsFromAdminNetwork => true;
}
