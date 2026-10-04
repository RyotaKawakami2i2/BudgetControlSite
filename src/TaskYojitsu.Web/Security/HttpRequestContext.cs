using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Infrastructure.Sessions;

namespace TaskYojitsu.Web.Security;

/// <summary>要求の情報（操作者、セッション、送信元）。送信元 IP は nginx から来た要求に限って X-Forwarded-For を使う。</summary>
public sealed class HttpRequestContext(IHttpContextAccessor accessor, AdminNetworks adminNetworks) : IRequestContext
{
    private HttpContext? Http => accessor.HttpContext;

    public Guid? UserId =>
        Http?.User.Identity?.IsAuthenticated == true
        && Guid.TryParse(Http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public Guid? SessionId => Http is { } h ? SessionInfo.From(h)?.Id : null;

    public DateTime? AuthTime => Http is { } h ? SessionInfo.From(h)?.AuthTime : null;

    public IPAddress? Ip => ClientIp.Of(Http);

    public string? UserAgent => Http?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    public string? RequestId => Http is { } h ? RequestIdMiddleware.Get(h) : null;

    public bool IsFromAdminNetwork => adminNetworks.Allows(Ip);
}

public static class ClientIp
{
    public static IPAddress? Of(HttpContext? context)
    {
        var ip = context?.Connection.RemoteIpAddress;
        return ip is { IsIPv4MappedToIPv6: true } ? ip.MapToIPv4() : ip;
    }
}

/// <summary>管理の操作を許可するネットワーク（Security:AdminAllowedNetworks。空なら制限しない。NF-ACC-07）。</summary>
public sealed class AdminNetworks
{
    private readonly List<IPNetwork> _networks;

    public AdminNetworks(IOptions<SecurityOptions> options)
    {
        _networks = [.. options.Value.AdminAllowedNetworks.Select(IPNetwork.Parse)];
    }

    public bool Allows(IPAddress? ip) =>
        _networks.Count == 0 || (ip is not null && _networks.Any(n => n.Contains(ip)));
}
