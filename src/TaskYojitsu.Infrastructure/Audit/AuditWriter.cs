using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Infrastructure.Persistence;

namespace TaskYojitsu.Infrastructure.Audit;

/// <summary>
/// 監査ログの書き込み（NF-LOG-01〜05）。日時（UTC）、操作者、操作の種類、対象、結果、送信元 IP、ブラウザの情報、リクエスト ID を記録する。
/// 直前の記録のハッシュ値は DB のトリガーが付ける。パスワードやトークンなどの秘密情報は detail に入れない。
/// </summary>
public sealed class AuditWriter(AppDbContext db, IDbContextFactory<AppDbContext> factory, IRequestContext request, BusinessClock clock) : IAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public void Add(AuditEntry entry) => db.AuditLogs.Add(ToEntity(entry));

    public async Task WriteNowAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        // 業務のトランザクションとは別に、すぐに記録する
        await using var separate = await factory.CreateDbContextAsync(cancellationToken);
        separate.AuditLogs.Add(ToEntity(entry));
        await separate.SaveChangesAsync(cancellationToken);
    }

    private AuditLog ToEntity(AuditEntry entry) => new()
    {
        OccurredAt = clock.UtcNow,
        ActorId = entry.ActorId ?? request.UserId,
        ActorHint = entry.ActorHint,
        Action = entry.Action,
        Result = entry.Result,
        TargetType = entry.TargetType,
        TargetId = entry.TargetId,
        TeamId = entry.TeamId,
        Ip = request.Ip,
        UserAgent = Truncate(request.UserAgent, 256),
        RequestId = Truncate(request.RequestId, 64),
        Detail = entry.Detail is null ? null : JsonSerializer.Serialize(entry.Detail, JsonOptions),
    };

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
