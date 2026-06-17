using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public sealed class DeployConfigService : IDeployConfigService
{
    private readonly AdminDbContext _db;

    public DeployConfigService(AdminDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DeployEnvConfigDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.DeployEnvConfigs
            .AsNoTracking()
            .OrderBy(c => c.EnvName)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DeployEnvConfigDto> UpsertAsync(UpsertDeployEnvConfigRequest req, CancellationToken ct = default)
    {
        var envName = (req.EnvName ?? "").Trim();
        if (string.IsNullOrEmpty(envName))
            throw new ArgumentException("envName is required", nameof(req));

        var now = DateTime.UtcNow;
        var row = await _db.DeployEnvConfigs.FirstOrDefaultAsync(c => c.EnvName == envName, ct);
        if (row is null)
        {
            row = new DeployEnvConfig { Id = Guid.NewGuid(), EnvName = envName, CreatedAt = now };
            _db.DeployEnvConfigs.Add(row);
        }

        row.ResourceGroup = (req.ResourceGroup ?? "").Trim();
        row.BpmSvcApp = (req.BpmSvcApp ?? "").Trim();
        row.AdminSvcApp = (req.AdminSvcApp ?? "").Trim();
        row.BpmUiSwa = (req.BpmUiSwa ?? "").Trim();
        row.AdminUiSwa = (req.AdminUiSwa ?? "").Trim();
        row.Enabled = req.Enabled;
        row.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static DeployEnvConfigDto ToDto(DeployEnvConfig c) => new(
        c.EnvName, c.ResourceGroup, c.BpmSvcApp, c.AdminSvcApp, c.BpmUiSwa, c.AdminUiSwa, c.Enabled);
}
