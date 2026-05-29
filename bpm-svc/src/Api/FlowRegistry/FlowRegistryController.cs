using Bpm.Api.Common;
using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.FlowRegistry;

/// <summary>
/// Read-only projection of <see cref="SharedFlow"/> for end-user UI.
/// Lets bpm-ui filter Quick Actions to the latest non-retired version
/// per flowCode, and lets case-detail screens label cases whose flow
/// has since been retired.
/// </summary>
[ApiController]
[Authorize]
[Route("api/flow-registry")]
public sealed class FlowRegistryController : BpmControllerBase
{
    private readonly AppDbContext _db;

    public FlowRegistryController(AppDbContext db) => _db = db;

    /// <summary>
    /// One entry per flow-version row. Filter caller-side; bpm-ui
    /// typically pairs this with its compile-time manifest map (only
    /// shows flows that have both an Approved registry row and a
    /// registered React form component).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FlowRegistryEntry>>> List(CancellationToken ct)
    {
        // LEFT JOIN against SharedFlowGroups so bpm-ui's launcher can
        // render group sections without a second round-trip per flow.
        // Soft-deleted groups still surface here (DeletedAt != null) —
        // the client renders them under the unassigned bucket so
        // existing assignments don't visually break.
        var rows = await _db.SharedFlows
            .AsNoTracking()
            .Where(f => f.DeletedAt == null)
            .OrderBy(f => f.FlowCode).ThenByDescending(f => f.Version)
            .Select(f => new
            {
                f.FlowCode, f.Version, f.State, f.DisplayName, f.UpdatedAt, f.GroupId,
                Group = f.GroupId == null
                    ? null
                    : _db.SharedFlowGroups
                        .Where(g => g.Id == f.GroupId && g.DeletedAt == null)
                        .Select(g => new { g.Code, g.DisplayNameJson, g.SortOrder, g.Icon })
                        .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var result = rows.Select(r => new FlowRegistryEntry(
            r.FlowCode, r.Version, r.State.ToString(), r.DisplayName, r.UpdatedAt,
            r.Group?.Code,
            r.Group is null ? null : ParseDisplayName(r.Group.DisplayNameJson),
            r.Group?.Icon,
            r.Group?.SortOrder)).ToList();
        return Ok(result);
    }

    private static IReadOnlyDictionary<string, string>? ParseDisplayName(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record FlowRegistryEntry(
    string FlowCode,
    int Version,
    string State,
    string DisplayName,
    DateTime UpdatedAt,
    string? GroupCode,
    IReadOnlyDictionary<string, string>? GroupDisplayName,
    string? GroupIcon,
    int? GroupSortOrder);
