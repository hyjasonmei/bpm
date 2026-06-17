using Bpm.Admin.Application.Flows;
using Bpm.Admin.Persistence.Flows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Persistence.Tests;

public class DeployConfigServiceTests
{
    private static (DeployConfigService svc, AdminDbContext ctx, SqliteConnection conn) CreateService()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connection).Options;
        var ctx = new AdminDbContext(options);
        ctx.Database.EnsureCreated();
        return (new DeployConfigService(ctx), ctx, connection);
    }

    [Fact]
    public async Task Upsert_then_List_round_trips()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            await svc.UpsertAsync(new UpsertDeployEnvConfigRequest(
                EnvName: "azure-poc",
                ResourceGroup: "rg-poc",
                BpmSvcApp: "poc-flowcook-api",
                AdminSvcApp: "poc-flowcook-admin-api",
                BpmUiSwa: "poc-flowcook-ui",
                AdminUiSwa: "poc-flowcook-admin-ui",
                Enabled: true));

            var all = await svc.ListAsync();
            var cfg = Assert.Single(all);
            Assert.Equal("azure-poc", cfg.EnvName);
            Assert.Equal("rg-poc", cfg.ResourceGroup);
            Assert.Equal("poc-flowcook-api", cfg.BpmSvcApp);
            Assert.Equal("poc-flowcook-admin-api", cfg.AdminSvcApp);
            Assert.Equal("poc-flowcook-ui", cfg.BpmUiSwa);
            Assert.Equal("poc-flowcook-admin-ui", cfg.AdminUiSwa);
            Assert.True(cfg.Enabled);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Upsert_same_env_updates_in_place_not_duplicate()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            await svc.UpsertAsync(new UpsertDeployEnvConfigRequest(
                "azure-poc", "rg-poc", "api-1", "admin-1", "ui-1", "admin-ui-1", true));
            await svc.UpsertAsync(new UpsertDeployEnvConfigRequest(
                "azure-poc", "rg-poc-2", "api-2", "admin-2", "ui-2", "admin-ui-2", false));

            var all = await svc.ListAsync();
            var cfg = Assert.Single(all);
            Assert.Equal("rg-poc-2", cfg.ResourceGroup);
            Assert.Equal("api-2", cfg.BpmSvcApp);
            Assert.False(cfg.Enabled);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }
}
