using System.Text.Json;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Sandbox;

public sealed class SandboxService(AppDbContext db, ILogger<SandboxService> logger) : ISandboxService
{
    private const string DefaultTenant = "default";

    public async Task<SandboxStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateAsync(ct);
        return ToStatus(settings);
    }

    public async Task<SandboxStatusDto> SetStatusAsync(UpdateSandboxRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var settings = await GetOrCreateAsync(ct);
        var previous = settings.SandboxMode;
        settings.SandboxMode = req.Enabled;
        settings.SandboxConfigJson = req.Config is null
            ? null
            : JsonSerializer.Serialize(req.Config);
        settings.SandboxLastToggledAt = DateTime.UtcNow;
        settings.SandboxLastToggledByUserId = actorUserId;
        await db.SaveChangesAsync(ct);
        // PR-J5 §12.1: audit row table is intentionally skipped (mirrors §4.8
        // clock-event decision); Info-level log carries actor + transition so
        // grep / log-aggregator captures the story.
        logger.LogInformation(
            "Sandbox toggle {Previous} → {Next} by {Actor} on tenant {Tenant} at {At:o}",
            previous, req.Enabled, actorUserId, settings.TenantCode, settings.SandboxLastToggledAt);
        return ToStatus(settings);
    }

    public async Task<IReadOnlyList<SandboxRedirectDto>> GetRedirectsAsync(int days, CancellationToken ct = default)
    {
        if (days < 1) days = 1;
        if (days > 90) days = 90;
        var since = DateTime.UtcNow.AddDays(-days);
        var rows = await db.SandboxRedirects.AsNoTracking()
            .Where(r => r.TenantCode == DefaultTenant && r.DispatchedAt >= since)
            .OrderByDescending(r => r.DispatchedAt)
            .Take(500)
            .ToListAsync(ct);

        return rows.Select(r => new SandboxRedirectDto(
            r.Id, r.Channel, r.Action,
            JsonSerializer.Deserialize<List<string>>(r.OriginalTargetsJson) ?? new(),
            JsonSerializer.Deserialize<List<string>>(r.RedirectedTargetsJson) ?? new(),
            r.SampleSubject, r.DispatchedAt)).ToList();
    }

    private async Task<TenantSettings> GetOrCreateAsync(CancellationToken ct)
    {
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == DefaultTenant, ct);
        if (s is not null) return s;
        s = new TenantSettings { TenantCode = DefaultTenant };
        db.TenantSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    private static SandboxStatusDto ToStatus(TenantSettings s)
    {
        SandboxConfigDto? config = null;
        if (!string.IsNullOrWhiteSpace(s.SandboxConfigJson))
        {
            try { config = JsonSerializer.Deserialize<SandboxConfigDto>(s.SandboxConfigJson); }
            catch { /* ignore malformed */ }
        }
        return new SandboxStatusDto(
            s.SandboxMode,
            config,
            s.SandboxLastToggledAt,
            s.SandboxLastToggledByUserId);
    }
}
