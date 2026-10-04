using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaskYojitsu.Application.Abstractions;
using TaskYojitsu.Application.Common;
using TaskYojitsu.Application.Security;
using TaskYojitsu.Domain.Codes;
using TaskYojitsu.Domain.Entities;
using TaskYojitsu.Domain.Rules;

namespace TaskYojitsu.Application.Views;

public sealed record ViewDto(
    Guid Id,
    string Name,
    bool IsShared,
    Guid? TeamId,
    string? TeamName,
    Guid OwnerId,
    string OwnerName,
    JsonElement Conditions,
    bool IsDefault,
    bool CanEdit,
    int Version);

public sealed record CreateViewRequest(string? Name, JsonElement? Conditions, bool IsShared, Guid? TeamId);

public sealed record UpdateViewRequest
{
    public int Version { get; init; }
    public Optional<string?> Name { get; init; }
    public Optional<JsonElement?> Conditions { get; init; }
}

public sealed record DefaultViewRequest(Guid? ViewId);

/// <summary>ビュー（FR-GNT-26、27。API-45〜48、API-03）。</summary>
public sealed partial class ViewService(IAppDbContext db, AccessPolicy access, IAuditWriter audit, BusinessClock clock)
{
    public async Task<IReadOnlyList<ViewDto>> ListAsync(CancellationToken ct)
    {
        var userId = access.UserId;
        var memberships = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.RemovedAt == null)
            .ToDictionaryAsync(m => m.TeamId, m => m.Role, ct);
        var teamIds = memberships.Keys.ToList();
        var defaultId = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DefaultViewId).SingleOrDefaultAsync(ct);
        var rows = await db.SavedViews.AsNoTracking()
            .Where(v => (!v.IsShared && v.OwnerId == userId) || (v.IsShared && v.TeamId != null && teamIds.Contains(v.TeamId.Value)))
            .Join(db.Users, v => v.OwnerId, u => u.Id, (v, u) => new { v, u.DisplayName })
            .ToListAsync(ct);
        var teamNames = await db.Teams.AsNoTracking().Where(t => teamIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return [.. rows
            .OrderBy(x => x.v.IsShared)
            .ThenBy(x => x.v.Name, StringComparer.Ordinal)
            .Select(x => ToDto(x.v, x.DisplayName, x.v.TeamId is { } t ? teamNames.GetValueOrDefault(t) : null, defaultId,
                CanEdit(x.v, userId, memberships)))];
    }

    public async Task<ViewDto> CreateAsync(CreateViewRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        var v = new Validation();
        var name = v.SingleLine("name", request.Name, Limits.ViewNameMax, required: true);
        var conditions = ValidateConditions(v, request.Conditions);
        v.ThrowIfAny();

        Guid? teamId = null;
        string? teamName = null;
        if (request.IsShared)
        {
            // 共有ビューは、リーダーがチームの全員のために作る
            if (request.TeamId is not { } id)
            {
                throw ValidationException.For("teamId", Msg.CmnRequired);
            }

            var team = await access.RequireTeamAsync(id, Operation.TeamSharedViewManage, ct);
            teamId = id;
            teamName = team.Team.Name;
        }

        var now = clock.UtcNow;
        var view = new SavedView
        {
            Id = Guid.CreateVersion7(),
            OwnerId = userId,
            TeamId = teamId,
            IsShared = request.IsShared,
            Name = name!,
            Conditions = conditions!,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SavedViews.Add(view);
        if (view.IsShared)
        {
            audit.Add(new AuditEntry("view.shared.created", TargetType: "view", TargetId: view.Id.ToString(), TeamId: teamId,
                Detail: new { name }));
        }

        await db.SaveChangesAsync(ct);
        var user = await access.GetUserAsync(ct);
        return ToDto(view, user.DisplayName, teamName, null, canEdit: true);
    }

    public async Task<ViewDto> UpdateAsync(Guid viewId, UpdateViewRequest request, CancellationToken ct)
    {
        var view = await LoadEditableAsync(viewId, ct);
        if (request.Version != view.Version)
        {
            throw new ConflictException();
        }

        var v = new Validation();
        if (request.Name.HasValue)
        {
            view.Name = v.SingleLine("name", request.Name.Value, Limits.ViewNameMax, required: true) ?? view.Name;
        }

        if (request.Conditions.HasValue)
        {
            view.Conditions = ValidateConditions(v, request.Conditions.Value) ?? view.Conditions;
        }

        v.ThrowIfAny();
        view.Version++;
        view.UpdatedAt = clock.UtcNow;
        if (view.IsShared)
        {
            audit.Add(new AuditEntry("view.shared.updated", TargetType: "view", TargetId: viewId.ToString(), TeamId: view.TeamId,
                Detail: new { view.Name }));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        var owner = await db.Users.AsNoTracking().Where(u => u.Id == view.OwnerId).Select(u => u.DisplayName).SingleAsync(ct);
        var teamName = view.TeamId is { } t ? await db.Teams.AsNoTracking().Where(x => x.Id == t).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
        var defaultId = await db.Users.AsNoTracking().Where(u => u.Id == access.UserId).Select(u => u.DefaultViewId).SingleAsync(ct);
        return ToDto(view, owner, teamName, defaultId, canEdit: true);
    }

    public async Task DeleteAsync(Guid viewId, CancellationToken ct)
    {
        var view = await LoadEditableAsync(viewId, ct);
        db.SavedViews.Remove(view);

        // 最初に開くビューに設定している人の設定を外す
        var users = await db.Users.Where(u => u.DefaultViewId == viewId).ToListAsync(ct);
        foreach (var u in users)
        {
            u.DefaultViewId = null;
        }

        if (view.IsShared)
        {
            audit.Add(new AuditEntry("view.shared.deleted", TargetType: "view", TargetId: viewId.ToString(), TeamId: view.TeamId,
                Detail: new { view.Name }));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>最初に開くビューの設定（API-03）。null で解除する。</summary>
    public async Task SetDefaultAsync(DefaultViewRequest request, CancellationToken ct)
    {
        var userId = access.UserId;
        if (request.ViewId is { } viewId)
        {
            var visible = (await ListAsync(ct)).Any(v => v.Id == viewId);
            if (!visible)
            {
                throw new NotFoundException();
            }
        }

        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.DefaultViewId = request.ViewId;
        await db.SaveChangesAsync(ct);
    }

    private async Task<SavedView> LoadEditableAsync(Guid viewId, CancellationToken ct)
    {
        var userId = access.UserId;
        var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == viewId, ct) ?? throw new NotFoundException();
        if (view.IsShared && view.TeamId is { } teamId)
        {
            await access.RequireTeamAsync(teamId, Operation.TeamSharedViewManage, ct);
        }
        else if (view.OwnerId != userId)
        {
            throw new NotFoundException();
        }

        return view;
    }

    private static bool CanEdit(SavedView view, Guid userId, Dictionary<Guid, TeamRole> memberships) =>
        view.IsShared
            ? view.TeamId is { } t && memberships.GetValueOrDefault(t) == TeamRole.Leader
            : view.OwnerId == userId;

    private static ViewDto ToDto(SavedView view, string ownerName, string? teamName, Guid? defaultId, bool canEdit)
    {
        using var doc = JsonDocument.Parse(view.Conditions);
        return new ViewDto(view.Id, view.Name, view.IsShared, view.TeamId, teamName, view.OwnerId, ownerName,
            doc.RootElement.Clone(), view.Id == defaultId, canEdit, view.Version);
    }

    // ---------------------------------------------------------------- 条件の検査（詳細設計書 7.3.6）

    private static readonly HashSet<string> Ranges = ["default", "this_month", "next_month", "this_quarter", "custom"];
    private static readonly HashSet<string> Groups = ["hierarchy", "assignee", "team", "status"];
    private static readonly HashSet<string> Sorts = ["manual", "planned_start", "planned_end", "priority", "progress"];
    private static readonly HashSet<string> Zooms = ["day", "week", "month", "quarter"];
    private static readonly HashSet<string> Colors = ["status", "assignee", "priority", "tag"];

    // よく使う条件「今週が期限」（FR-GNT-21）
    private static readonly HashSet<string> DueChoices = ["this_week"];
    private static readonly HashSet<string> Columns =
        ["assignee", "status", "progress", "planned_dates", "actual_dates", "planned_minutes", "actual_minutes", "variance", "priority", "tags"];

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex DatePattern();

    /// <summary>決められた項目だけで、値が選択肢か UUID の形式で、配列が 50 個以内で、全体が 4KB 以内であることを確かめる。</summary>
    internal static string? ValidateConditions(Validation v, JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Object } element)
        {
            v.Add("conditions", Msg.CmnChoice);
            return null;
        }

        var ok = true;
        var output = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            JsonNode? node = property.Name switch
            {
                "v" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var ver) && ver == 1 ? JsonValue.Create(1) : null,
                "teams" or "tags" => Array(value, s => UuidPattern().IsMatch(s)),
                "assignees" => Array(value, s => s is "me" or "unassigned" || UuidPattern().IsMatch(s)),
                "status" => Array(value, s => EnumCodes.TryParse<TaskItemStatus>(s, out _)),
                "priority" => Array(value, s => EnumCodes.TryParse<Priority>(s, out _)),
                "flags" => Array(value, s => EnumCodes.TryParse<DelayFlag>(s, out _)),
                "cols" => Array(value, Columns.Contains),
                "range" => Choice(value, Ranges),
                "group" => Choice(value, Groups),
                "sort" => Choice(value, Sorts),
                "zoom" => Choice(value, Zooms),
                "color" => Choice(value, Colors),
                "due" => Choice(value, DueChoices),
                "from" or "to" => value.ValueKind == JsonValueKind.String && DatePattern().IsMatch(value.GetString()!)
                    && DateOnly.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out _)
                    ? JsonValue.Create(value.GetString()) : null,
                "milestones" or "dense" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? JsonValue.Create(value.GetBoolean()) : null,
                "q" => value.ValueKind == JsonValueKind.String && value.GetString() is { } q
                    && TextRules.NormalizeSingleLine(q) is var normalized && (normalized is null || TextRules.Length(normalized) <= 100)
                    ? JsonValue.Create(normalized ?? "") : null,
                _ => null,
            };

            if (node is null)
            {
                ok = false;
                break;
            }

            output[property.Name] = node;
        }

        if (!ok)
        {
            v.Add("conditions", Msg.CmnChoice);
            return null;
        }

        output["v"] = 1;
        var json = output.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) > Limits.ViewConditionsMaxBytes)
        {
            v.Add("conditions", Msg.CmnTooLong);
            return null;
        }

        return json;
    }

    private static JsonArray? Array(JsonElement value, Func<string, bool> isValid)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 50)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } s || !isValid(s))
            {
                return null;
            }

            array.Add(s);
        }

        return array;
    }

    private static JsonValue? Choice(JsonElement value, HashSet<string> choices) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { } s && choices.Contains(s) ? JsonValue.Create(s) : null;
}
