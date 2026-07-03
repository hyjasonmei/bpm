using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;
using Bpm.Admin.Application.Datasets;
using Bpm.Admin.Persistence.Datasets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Persistence.Tests;

public class DatasetServiceTests
{
    private sealed class StubClock : IClock
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayInTaipei() => new(2026, 1, 1);
    }

    private sealed class NoopAudit : IAuditLogger
    {
        public Task LogAsync(string actionType, string targetType, string? targetId,
            Guid? actorUserId, Guid? actorPrincipalId, object? before = null, object? after = null,
            string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static (DatasetService svc, AdminDbContext ctx, SqliteConnection conn) CreateService()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connection).Options;
        var ctx = new AdminDbContext(options);
        ctx.Database.EnsureCreated();
        return (new DatasetService(ctx, new StubClock(), new NoopAudit()), ctx, connection);
    }

    [Fact]
    public async Task CreateDataset_then_AddRow_persists_and_lists()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var ds = await svc.CreateAsync(new CreateDatasetRequest(
                "tw-regions", "台灣行政區劃", null,
                new[] { new DatasetColumnDef("city", "縣市", "text"),
                        new DatasetColumnDef("district", "行政區", "text") }), null);

            await svc.AddRowAsync(ds.Id, new AddRowRequest(
                new Dictionary<string, string> { ["city"] = "台北市", ["district"] = "大安區" }), null);

            var rows = await svc.ListRowsAsync(ds.Id);
            Assert.Single(rows);
            Assert.Equal("大安區", rows[0].Cells["district"]);
            Assert.True(rows[0].IsActive);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task DuplicateKey_throws()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            await svc.CreateAsync(new CreateDatasetRequest("k", "A", null, System.Array.Empty<DatasetColumnDef>()), null);
            await Assert.ThrowsAsync<DatasetException>(() =>
                svc.CreateAsync(new CreateDatasetRequest("k", "B", null, System.Array.Empty<DatasetColumnDef>()), null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }
}
