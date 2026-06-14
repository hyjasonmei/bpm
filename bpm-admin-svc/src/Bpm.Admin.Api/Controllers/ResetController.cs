using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Seed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

/// <summary>
/// Demo "factory reset" for the admin-owned identity tables. Truncates
/// Principals / Roles / Flows / memberships / delegations and re-seeds the
/// canonical org graph (13 users / 6 depts / 1 group / 15 roles + grants).
///
/// The shared SQLite file's bpm-owned runtime tables (flow cases,
/// notifications, captured mail) are NOT touched here — the admin-ui Reset
/// tab orchestrates the full sequence: bpm-svc factory-wipe → this reseed →
/// re-register + publish flows. Destructive; the UI guards with a
/// type-to-confirm dialog.
/// </summary>
// Destructive demo reset — gate to the "SystemAdmin" policy (RequireClaim on the
// "roles" claim; see Program.cs). Only Jack is seeded with SYSTEM_ADMIN, which is
// the admin-ui login. Authorization runs at request start (before ClearOrgAsync),
// so the in-flight token stays valid through the wipe.
// NOTE: the rest of admin-svc's controllers are currently un-gated — broader
// hardening is a separate decision; this locks only the destructive endpoint.
[ApiController]
[Route("api/admin/reset")]
[Authorize(Policy = "SystemAdmin")]
public sealed class ResetController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IConfiguration _config;

    public ResetController(AdminDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("reseed")]
    public async Task<IActionResult> Reseed(CancellationToken ct)
    {
        var connectionString = _config.GetConnectionString("Admin")
            ?? _config.GetConnectionString("Default")
            ?? "Data Source=bpm.db";
        await Seeder.ResetOrgAsync(DbPathResolver.Normalize(connectionString));

        var users = await _db.Principals.CountAsync(p => p.Type == PrincipalType.User, ct);
        var roles = await _db.Roles.CountAsync(ct);
        return Ok(new { ok = true, users, roles });
    }
}
