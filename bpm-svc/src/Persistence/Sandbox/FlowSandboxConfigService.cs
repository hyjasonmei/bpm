using System.Text.RegularExpressions;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Sandbox;

/// <summary>
/// Per-flow sandbox scope over the shared <see cref="AppDbContext"/>. Deployed
/// flows are auto-discovered by the same EF-model reflection the reports /
/// flow-codes controllers use — a new chef-cooked flow shows up here the moment
/// its <c>&lt;CODE&gt;_V&lt;N&gt;_Case</c> table is registered, with no code change.
/// </summary>
public sealed class FlowSandboxConfigService(AppDbContext db) : IFlowSandboxConfigService
{
    private const string DefaultTenant = "default";

    // Same matcher as FlowCodesController / ReportsController: any version of a
    // flow case entity, version stripped so APE_V1_Case and a future
    // APE_V2_Case both collapse to "APE".
    private static readonly Regex CaseTypeRe = new(@"^(?<code>.+)_V\d+_Case$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<FlowSandboxStateDto>> ListAsync(CancellationToken ct = default)
    {
        var codes = DeployedFlowCodes();
        var cfgs = await db.Set<FlowSandboxConfig>().AsNoTracking()
            .Where(c => c.TenantCode == DefaultTenant)
            .ToListAsync(ct);
        var map = cfgs.ToDictionary(c => c.FlowCode, c => c.CaptureEnabled, StringComparer.Ordinal);

        return codes
            .Select(c => new FlowSandboxStateDto(c, c, map.GetValueOrDefault(c)))
            .ToList();
    }

    public async Task<FlowSandboxStateDto> SetCaptureAsync(string flowCode, bool enabled, CancellationToken ct = default)
    {
        var cfg = await db.Set<FlowSandboxConfig>()
            .FirstOrDefaultAsync(c => c.TenantCode == DefaultTenant && c.FlowCode == flowCode, ct);
        if (cfg is null)
        {
            cfg = new FlowSandboxConfig
            {
                Id = Guid.NewGuid(),
                TenantCode = DefaultTenant,
                FlowCode = flowCode,
                CaptureEnabled = enabled,
            };
            db.Set<FlowSandboxConfig>().Add(cfg);
        }
        else
        {
            cfg.CaptureEnabled = enabled;
        }
        await db.SaveChangesAsync(ct);
        return new FlowSandboxStateDto(flowCode, flowCode, enabled);
    }

    public async Task<bool> IsCaptureEffectiveAsync(string flowCode, CancellationToken ct = default)
    {
        var settings = await db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == DefaultTenant, ct);
        if (settings is { SandboxMode: true }) return true;

        var cfg = await db.Set<FlowSandboxConfig>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantCode == DefaultTenant && c.FlowCode == flowCode, ct);
        return cfg?.CaptureEnabled ?? false;
    }

    private IReadOnlyList<string> DeployedFlowCodes()
        => db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Select(t => new { Type = t, Match = CaseTypeRe.Match(t.Name) })
            .Where(x => x.Match.Success
                        && x.Type.GetProperty("Status") is not null
                        && x.Type.GetProperty("SubmittedAt") is not null)
            .Select(x => x.Match.Groups["code"].Value)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
}
