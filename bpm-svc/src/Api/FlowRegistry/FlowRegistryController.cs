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
        var rows = await _db.SharedFlows
            .AsNoTracking()
            .Where(f => f.DeletedAt == null)
            .OrderBy(f => f.FlowCode).ThenByDescending(f => f.Version)
            .Select(f => new FlowRegistryEntry(
                f.FlowCode, f.Version, f.State.ToString(), f.DisplayName, f.UpdatedAt))
            .ToListAsync(ct);
        return Ok(rows);
    }
}

public sealed record FlowRegistryEntry(
    string FlowCode,
    int Version,
    string State,
    string DisplayName,
    DateTime UpdatedAt);
