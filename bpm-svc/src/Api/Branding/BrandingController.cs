using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Branding;

/// <summary>
/// White-label branding for this customer deployment — system name + logo +
/// favicon, stored on the single "default" <see cref="TenantSettings"/> row.
///
/// <para>GET is anonymous so bpm-ui can paint the brand on the login screen
/// before anyone authenticates. PUT is written from admin's Site Setting →
/// Branding tab.</para>
///
/// <para>POC: PUT is left open (no auth) because admin-svc and bpm-svc use
/// separate auth realms and there's no token bridge yet. Lock this down once
/// the admin → bpm write path is authenticated.</para>
/// </summary>
[ApiController]
[Route("api/branding")]
[AllowAnonymous]
public sealed class BrandingController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<BrandingDto> Get(CancellationToken ct)
    {
        var t = await db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantCode == "default", ct);
        return new BrandingDto(t?.SystemName, t?.LogoDataUri, t?.FaviconDataUri);
    }

    [HttpPut]
    public async Task<BrandingDto> Put([FromBody] BrandingDto req, CancellationToken ct)
    {
        var t = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == "default", ct);
        if (t is null)
        {
            t = new TenantSettings { TenantCode = "default" };
            db.TenantSettings.Add(t);
        }

        t.SystemName = Clean(req.SystemName);
        t.LogoDataUri = Clean(req.LogoDataUri);
        t.FaviconDataUri = Clean(req.FaviconDataUri);
        await db.SaveChangesAsync(ct);

        return new BrandingDto(t.SystemName, t.LogoDataUri, t.FaviconDataUri);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record BrandingDto(string? SystemName, string? LogoDataUri, string? FaviconDataUri);
