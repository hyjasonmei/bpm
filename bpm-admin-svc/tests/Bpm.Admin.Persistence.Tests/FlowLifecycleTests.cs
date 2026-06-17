using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Persistence.Audit;
using Bpm.Admin.Persistence.Flows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Persistence.Tests;

public class FlowLifecycleTests
{
    private static (FlowLifecycleService svc, AdminDbContext ctx, SqliteConnection conn) CreateService()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connection).Options;
        var ctx = new AdminDbContext(options);
        ctx.Database.EnsureCreated();
        return (new FlowLifecycleService(ctx, new AuditLogger(ctx)), ctx, connection);
    }

    private static Flow SeedFlow(AdminDbContext ctx, string code, FlowState state, DateTime? archivedAt = null)
    {
        var row = new Flow
        {
            Id = Guid.NewGuid(),
            LineageId = Guid.NewGuid(),
            Version = 1,
            State = state,
            FlowCode = code,
            DisplayName = code,
            ArchivedAt = archivedAt,
        };
        ctx.Flows.Add(row);
        ctx.SaveChanges();
        return row;
    }

    [Fact]
    public async Task CreateDraft_Blocks_When_Active_Flow_Has_Same_Code()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            SeedFlow(ctx, "LEAVE", FlowState.Draft);
            var ex = await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.CreateDraftAsync("LEAVE", "Leave Request", null, null));
            Assert.Contains("LEAVE", ex.Message);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task CreateDraft_Blocks_Case_Insensitively()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            SeedFlow(ctx, "LEAVE", FlowState.Published);
            await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.CreateDraftAsync("leave", "Leave Request", null, null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task CreateDraft_Allows_Code_Of_Retired_Flow()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            SeedFlow(ctx, "LEAVE", FlowState.Retired);
            var row = await svc.CreateDraftAsync("LEAVE", "Leave Request", null, null);
            Assert.Equal("LEAVE", row.FlowCode);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task CreateDraft_Allows_Code_Of_Archived_Flow()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            SeedFlow(ctx, "LEAVE", FlowState.Approved, archivedAt: DateTime.UtcNow);
            var row = await svc.CreateDraftAsync("LEAVE", "Leave Request", null, null);
            Assert.Equal("LEAVE", row.FlowCode);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Theory]
    [InlineData(FlowState.Draft)]
    [InlineData(FlowState.Submitted)]
    [InlineData(FlowState.Cooking)]
    [InlineData(FlowState.OnHold)]
    [InlineData(FlowState.Committed)]
    [InlineData(FlowState.Approved)]
    [InlineData(FlowState.Rejected)]
    public async Task SoftDelete_Allows_Any_PrePublish_State(FlowState state)
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", state);
            await svc.SoftDeleteAsync(row.Id, null);

            var deleted = await ctx.Flows.IgnoreQueryFilters().SingleAsync(f => f.Id == row.Id);
            Assert.NotNull(deleted.DeletedAt);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Theory]
    [InlineData(FlowState.Published)]
    [InlineData(FlowState.Retired)]
    public async Task SoftDelete_Blocks_Published_And_Retired(FlowState state)
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", state);
            await Assert.ThrowsAsync<FlowLifecycleException>(() => svc.SoftDeleteAsync(row.Id, null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task SoftDelete_Frees_FlowCode_For_New_Draft()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Cooking);
            await svc.SoftDeleteAsync(row.Id, null);

            var recreated = await svc.CreateDraftAsync("LEAVE", "Leave Request", null, null);
            Assert.Equal("LEAVE", recreated.FlowCode);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task SoftDelete_Of_One_Version_Does_Not_Free_Code_Held_By_Another_Active_Version()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            SeedFlow(ctx, "LEAVE", FlowState.Published);
            var v2 = SeedFlow(ctx, "LEAVE", FlowState.Draft);
            await svc.SoftDeleteAsync(v2.Id, null);

            await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.CreateDraftAsync("LEAVE", "Leave Request", null, null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task CreateDraft_Trims_And_Uppercases_Before_Clash_Check()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            SeedFlow(ctx, "LEAVE", FlowState.Draft);
            await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.CreateDraftAsync("  leave  ", "Leave Request", null, null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    // ── PR-CA1: publish gated on merge ──────────────────────────────

    [Fact]
    public async Task Publish_Blocks_When_Not_Merged()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
            var ex = await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.PublishAsync(row.Id, null));
            Assert.Contains("merge", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Publish_Moves_Approved_Merged_Flow_To_Publishing_Not_Published()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
            await svc.MarkMergedAsync(row.Id, null, "test");
            var result = await svc.PublishAsync(row.Id, null);
            Assert.Equal(FlowState.Publishing, result.State);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Publish_Retry_From_PublishFailed_Goes_To_Publishing()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.PublishFailed);
            await svc.MarkMergedAsync(row.Id, null, "test");
            var result = await svc.PublishAsync(row.Id, null);
            Assert.Equal(FlowState.Publishing, result.State);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task MarkPublished_From_Publishing_Sets_Published_And_PublishedAt()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Publishing);
            var result = await svc.MarkPublishedAsync(row.Id, null);
            Assert.Equal(FlowState.Published, result.State);
            Assert.NotNull(result.PublishedAt);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Theory]
    [InlineData(FlowState.Approved)]
    [InlineData(FlowState.Published)]
    [InlineData(FlowState.PublishFailed)]
    [InlineData(FlowState.Committed)]
    public async Task MarkPublished_Only_Allowed_From_Publishing(FlowState state)
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", state);
            await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.MarkPublishedAsync(row.Id, null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task MarkPublishFailed_From_Publishing_Sets_PublishFailed_And_Reason()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Publishing);
            var result = await svc.MarkPublishFailedAsync(row.Id, "az deploy timed out", null);
            Assert.Equal(FlowState.PublishFailed, result.State);
            Assert.Equal("az deploy timed out", result.PublishFailedReason);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Theory]
    [InlineData(FlowState.Approved)]
    [InlineData(FlowState.Published)]
    [InlineData(FlowState.PublishFailed)]
    public async Task MarkPublishFailed_Only_Allowed_From_Publishing(FlowState state)
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", state);
            await Assert.ThrowsAsync<FlowLifecycleException>(
                () => svc.MarkPublishFailedAsync(row.Id, "boom", null));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task MarkMerged_Is_Idempotent()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
            var first = await svc.MarkMergedAsync(row.Id, null, "test");
            var stamp = first.MergedAt;
            var second = await svc.MarkMergedAsync(row.Id, null, "test");
            Assert.Equal(stamp, second.MergedAt);   // not overwritten
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task SetPrUrl_Persists_And_Is_Idempotent()
    {
        var (svc, ctx, conn) = CreateService();
        try
        {
            var row = SeedFlow(ctx, "LEAVE", FlowState.Approved);
            await svc.SetPrUrlAsync(row.Id, "https://github.com/x/y/pull/1");
            await svc.SetPrUrlAsync(row.Id, "https://github.com/x/y/pull/1"); // no throw
            var fresh = await ctx.Flows.AsNoTracking().SingleAsync(f => f.Id == row.Id);
            Assert.Equal("https://github.com/x/y/pull/1", fresh.PrUrl);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }
}
