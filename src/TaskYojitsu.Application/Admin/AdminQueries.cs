using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Gantt;
using TaskYojitsu.Application.Tasks;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Admin;

public sealed record AdminTeamDto(
    Guid Id,
    string Name,
    string? Description,
    bool Archived,
    int MemberCount,
    IReadOnlyList<string> Leaders,
    DateTime CreatedAt);

public sealed record AuditLogDto(
    long Id,
    DateTime OccurredAt,
    Guid? ActorId,
    string? ActorName,
    string Action,
    AuditResult Result,
    string? TargetType,
    string? TargetId,
    Guid? TeamId,
    string? Ip,
    string? UserAgent,
    string? RequestId,
    JsonElement? Detail);

public sealed record AuditLogSearch(
    DateTime? From,
    DateTime? To,
    Guid? ActorId,
    string? Action,
    string? TargetType,
    string? TargetId,
    string? Cursor,
    int? Limit);

public sealed record HolidayRequest(DateOnly? Date, string? Name);

/// <summary>全チームの一覧（API-61）。</summary>
public sealed class AdminTeamQuery(IAppDbContext db)
{
    public async Task<IReadOnlyList<AdminTeamDto>> ListAsync(CancellationToken ct)
    {
        var teams = await db.Teams.AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Description,
                t.ArchivedAt,
                t.CreatedAt,
                Members = db.TeamMembers.Count(m => m.TeamId == t.Id && m.RemovedAt == null),
            })
            .ToListAsync(ct);
        var leaders = await db.TeamMembers.AsNoTracking()
            .Where(m => m.RemovedAt == null && m.Role == TeamRole.Leader)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.TeamId, u.DisplayName })
            .ToListAsync(ct);
        var byTeam = leaders.ToLookup(l => l.TeamId, l => l.DisplayName);
        return [.. teams
            .OrderBy(t => t.ArchivedAt is not null)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new AdminTeamDto(t.Id, t.Name, t.Description, t.ArchivedAt is not null, t.Members,
                [.. byTeam[t.Id].Order(StringComparer.Ordinal)], t.CreatedAt))];
    }
}

/// <summary>監査ログの検索（FR-ADM-06。API-62）。検索したこと自体も記録する。</summary>
public sealed class AuditLogQuery(IAppDbContext db, IAuditWriter audit)
{
    public async Task<CursorPage<AuditLogDto>> SearchAsync(AuditLogSearch search, CancellationToken ct)
    {
        var take = Math.Clamp(search.Limit ?? 50, 1, 100);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (search.From is { } from)
        {
            var utc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
            query = query.Where(a => a.OccurredAt >= utc);
        }

        if (search.To is { } to)
        {
            var utc = DateTime.SpecifyKind(to, DateTimeKind.Utc);
            query = query.Where(a => a.OccurredAt < utc);
        }

        if (search.ActorId is { } actor)
        {
            query = query.Where(a => a.ActorId == actor);
        }

        if (!string.IsNullOrWhiteSpace(search.Action))
        {
            var action = search.Action.Trim();
            query = action.EndsWith('*')
                ? query.Where(a => a.Action.StartsWith(action.TrimEnd('*')))
                : query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(search.TargetType))
        {
            query = query.Where(a => a.TargetType == search.TargetType);
        }

        if (!string.IsNullOrWhiteSpace(search.TargetId))
        {
            query = query.Where(a => a.TargetId == search.TargetId);
        }

        if (long.TryParse(search.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var before))
        {
            query = query.Where(a => a.Id < before);
        }

        var rows = await query.OrderByDescending(a => a.Id).Take(take + 1).ToListAsync(ct);
        var page = rows.Take(take).ToList();
        var actorIds = page.Where(a => a.ActorId is not null).Select(a => a.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        await audit.WriteNowAsync(new AuditEntry("audit.viewed", Detail: new
        {
            search.From,
            search.To,
            search.ActorId,
            search.Action,
            search.TargetType,
            search.TargetId,
        }), ct);

        return new CursorPage<AuditLogDto>(
            [.. page.Select(a => new AuditLogDto(
                a.Id, a.OccurredAt, a.ActorId, a.ActorId is { } id ? names.GetValueOrDefault(id) : null, a.Action, a.Result,
                a.TargetType, a.TargetId, a.TeamId, a.Ip?.ToString(), a.UserAgent, a.RequestId,
                a.Detail is null ? null : JsonDocument.Parse(a.Detail).RootElement.Clone()))],
            rows.Count > take ? page[^1].Id.ToString(CultureInfo.InvariantCulture) : null);
    }
}

/// <summary>祝日の管理（FR-ADM-07。API-63〜65）。</summary>
public sealed class HolidayService(IAppDbContext db, IAuditWriter audit, IRequestContext request, BusinessClock clock)
{
    public async Task<IReadOnlyList<HolidayDto>> ListAsync(int? year, CancellationToken ct)
    {
        var query = db.Holidays.AsNoTracking();
        if (year is { } y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            query = query.Where(h => h.HolidayDate >= from && h.HolidayDate <= to);
        }

        return await query.OrderBy(h => h.HolidayDate).Select(h => new HolidayDto(h.HolidayDate, h.Name)).ToListAsync(ct);
    }

    public async Task<HolidayDto> AddAsync(HolidayRequest input, CancellationToken ct)
    {
        var v = new Validation();
        if (input.Date is not { } date || date < Limits.MinDate || date > Limits.MaxDate)
        {
            v.Add("date", Msg.CmnChoice);
            date = default;
        }

        var name = v.SingleLine("name", input.Name, 50, required: true);
        v.ThrowIfAny();
        var existing = await db.Holidays.SingleOrDefaultAsync(h => h.HolidayDate == date, ct);
        if (existing is null)
        {
            db.Holidays.Add(new Holiday { HolidayDate = date, Name = name!, CreatedAt = clock.UtcNow, CreatedBy = request.UserId });
        }
        else
        {
            existing.Name = name!;
        }

        audit.Add(new AuditEntry("holiday.created", TargetType: "holiday", TargetId: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Detail: new { name }));
        await db.SaveChangesAsync(ct);
        return new HolidayDto(date, name!);
    }

    public async Task DeleteAsync(DateOnly date, CancellationToken ct)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(h => h.HolidayDate == date, ct) ?? throw new NotFoundException();
        db.Holidays.Remove(holiday);
        audit.Add(new AuditEntry("holiday.deleted", TargetType: "holiday", TargetId: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Detail: new { holiday.Name }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 内閣府が公表する祝日の CSV（「国民の祝日・休日月日,国民の祝日・休日名称」）を取り込む（運用コマンド import-holidays）。
    /// 文字コードは呼び出し側で解釈する。日付は「2026/1/1」または「2026-01-01」の形式。
    /// </summary>
    public async Task<int> ImportAsync(IEnumerable<string> lines, CancellationToken ct)
    {
        var parsed = new Dictionary<DateOnly, string>();
        foreach (var line in lines)
        {
            var parts = line.Split(',', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var dateText = parts[0].Trim().Trim('"');
            if (!DateOnly.TryParseExact(dateText, ["yyyy/M/d", "yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var name = TextRules.NormalizeSingleLine(parts[1].Trim().Trim('"'));
            if (name is not null && TextRules.Length(name) <= 50)
            {
                parsed[date] = name;
            }
        }

        var dates = parsed.Keys.ToList();
        var existing = await db.Holidays.Where(h => dates.Contains(h.HolidayDate)).ToDictionaryAsync(h => h.HolidayDate, ct);
        var now = clock.UtcNow;
        foreach (var (date, name) in parsed)
        {
            if (existing.TryGetValue(date, out var holiday))
            {
                holiday.Name = name;
            }
            else
            {
                db.Holidays.Add(new Holiday { HolidayDate = date, Name = name, CreatedAt = now, CreatedBy = request.UserId });
            }
        }

        audit.Add(new AuditEntry("holiday.imported", TargetType: "holiday", Detail: new { count = parsed.Count }));
        await db.SaveChangesAsync(ct);
        return parsed.Count;
    }
}
