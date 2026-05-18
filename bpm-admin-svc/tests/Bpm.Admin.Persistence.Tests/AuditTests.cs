using Bpm.Admin.Domain.Audit;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Persistence.Tests;

public class AuditTests
{
    private static (AdminDbContext ctx, SqliteConnection conn) CreateContext(bool withInterceptor = true)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var optionsBuilder = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connection);
        if (withInterceptor)
        {
            optionsBuilder.AddInterceptors(new AuditingSaveChangesInterceptor());
        }
        var ctx = new AdminDbContext(optionsBuilder.Options);
        ctx.Database.EnsureCreated();
        return (ctx, connection);
    }

    [Fact]
    public async Task Cannot_Update_AuditEvent()
    {
        var (ctx, conn) = CreateContext(withInterceptor: false);
        try
        {
            var evt = new AuditEvent
            {
                EventId = Guid.NewGuid(),
                ActionType = "test",
                TargetType = "test",
                Timestamp = DateTime.UtcNow,
                SourceSystem = "admin",
            };
            ctx.AuditEvents.Add(evt);
            await ctx.SaveChangesAsync();

            evt.ActionType = "tampered";
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Cannot_Delete_AuditEvent()
    {
        var (ctx, conn) = CreateContext(withInterceptor: false);
        try
        {
            var evt = new AuditEvent
            {
                EventId = Guid.NewGuid(),
                ActionType = "test",
                TargetType = "test",
                Timestamp = DateTime.UtcNow,
                SourceSystem = "admin",
            };
            ctx.AuditEvents.Add(evt);
            await ctx.SaveChangesAsync();

            ctx.AuditEvents.Remove(evt);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Interceptor_Captures_Principal_Created()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var p = new Principal { Type = PrincipalType.User, DisplayName = "AuditedAlice" };
            ctx.Principals.Add(p);
            await ctx.SaveChangesAsync();

            var events = await ctx.AuditEvents.Where(e => e.TargetType == "Principal").ToListAsync();
            Assert.Single(events);
            var ev = events[0];
            Assert.Equal("entity_created", ev.ActionType);
            Assert.Equal("admin", ev.SourceSystem);
            Assert.Equal(p.Id.ToString(), ev.TargetId);
            Assert.Null(ev.BeforeJson);
            Assert.NotNull(ev.AfterJson);
            Assert.Contains("AuditedAlice", ev.AfterJson);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Interceptor_Captures_Principal_Updated_With_Before_And_After()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var p = new Principal { Type = PrincipalType.User, DisplayName = "OriginalName" };
            ctx.Principals.Add(p);
            await ctx.SaveChangesAsync();

            p.DisplayName = "NewName";
            await ctx.SaveChangesAsync();

            var events = await ctx.AuditEvents
                .Where(e => e.TargetType == "Principal")
                .OrderBy(e => e.Timestamp)
                .ToListAsync();
            Assert.True(events.Count >= 2);
            var updateEvent = events.Last();
            Assert.Equal("entity_updated", updateEvent.ActionType);
            Assert.NotNull(updateEvent.BeforeJson);
            Assert.Contains("OriginalName", updateEvent.BeforeJson);
            Assert.NotNull(updateEvent.AfterJson);
            Assert.Contains("NewName", updateEvent.AfterJson);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Interceptor_Does_Not_Audit_NonAuditable_Entity()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            // UserSession is not IAuditable; create one and verify no audit event emitted
            ctx.UserSessions.Add(new Bpm.Admin.Domain.Auth.UserSession
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            await ctx.SaveChangesAsync();

            var events = await ctx.AuditEvents
                .Where(e => e.TargetType == "UserSession")
                .ToListAsync();
            Assert.Empty(events);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }
}
