using System.Security.Claims;
using Bpm.Api.Auth;
using Bpm.Api.Sandbox;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Sandbox;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Sandbox;

/// <summary>
/// PR-J5 §10.1 backend: <c>GET /api/sandbox/personas</c>. Asserts:
///  - sandbox-off returns silent empty list (no 403, so the dropdown can poll
///    without leaking auth errors)
///  - sandbox-on returns the seeded users with department names denormalised
///    (no second round-trip needed by the dropdown)
/// </summary>
public sealed class SandboxPersonasTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _stub = new();

    public SandboxPersonasTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_stub, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewCtx() => new(_options);

    private async Task SetSandboxAsync(bool on)
    {
        await using var db = NewCtx();
        var existing = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == "default");
        if (existing is null)
        {
            existing = new TenantSettings { TenantCode = "default" };
            db.TenantSettings.Add(existing);
        }
        existing.SandboxMode = on;
        await db.SaveChangesAsync();
    }

    private async Task SeedUsersAsync()
    {
        await using var db = NewCtx();
        var dept = new Department { Id = Guid.NewGuid(), Code = "OPS", Name = "Operations" };
        db.Departments.Add(dept);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "alice@ex.com", FullName = "Alice", DisplayName = "Alice",
            DepartmentId = dept.Id, IsActive = true,
        });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "bob@ex.com", FullName = "Bob", DisplayName = "Bob",
            IsActive = true,
        });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = "carol@ex.com", FullName = "Carol", DisplayName = "Carol",
            IsActive = false, // soft-deleted; should be filtered out
        });
        await db.SaveChangesAsync();
    }

    private SandboxController BuildController(out AppDbContext db)
    {
        db = NewCtx();
        var sys = new SystemClock();
        var sandboxClock = new SandboxClock(db, sys);
        var clockService = new SandboxClockService(
            db, sys, sandboxClock, new NoOpScheduledJobKicker(),
            NullLogger<SandboxClockService>.Instance);
        var resetService = new ResetService(db, clockService, NullLogger<ResetService>.Instance);
        var mailbox = new MailboxService(db, clockService);
        var jwt = new JwtTokenService(
            new JwtOptions { Secret = "test-secret-".PadRight(64, 'x') },
            new TestEnv());
        var ctrl = new SandboxController(
            new SandboxService(db, NullLogger<SandboxService>.Instance),
            clockService, resetService, mailbox, db, jwt);
        ctrl.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = HttpContextFor(Guid.NewGuid()),
        };
        return ctrl;
    }

    [Fact]
    public async Task ListPersonas_when_sandbox_off_returns_empty()
    {
        await SeedUsersAsync();
        await SetSandboxAsync(on: false);
        var ctrl = BuildController(out var db);
        try
        {
            var result = await ctrl.ListPersonas(default);
            Assert.Empty(result);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListPersonas_when_sandbox_on_returns_active_users_with_department()
    {
        await SeedUsersAsync();
        await SetSandboxAsync(on: true);
        var ctrl = BuildController(out var db);
        try
        {
            var result = await ctrl.ListPersonas(default);
            // Carol is inactive — skipped; Alice + Bob remain.
            Assert.Equal(2, result.Count);
            var alice = result.First(r => r.Email == "alice@ex.com");
            Assert.Equal("Alice", alice.FullName);
            Assert.Equal("Operations", alice.DepartmentName);
            var bob = result.First(r => r.Email == "bob@ex.com");
            Assert.Null(bob.DepartmentName);
        }
        finally { db.Dispose(); }
    }

    private static DefaultHttpContext HttpContextFor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("roles", "employee"),
        }, authenticationType: "test", nameType: "sub", roleType: "roles");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
