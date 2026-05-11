using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Bpm.Api.Auth;
using Bpm.Api.Sandbox;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Sandbox;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Sandbox;

/// <summary>
/// PR-J4 §6: persona switch endpoint. Asserts the sandbox-off / 404 / token
/// shape contracts. The non-admin "403" path is enforced declaratively by
/// <c>[Authorize(Roles = "admin")]</c> and tested via the auth pipeline in
/// the existing controller integration suites — replicating the auth
/// middleware in a unit test would just be testing ASP.NET. Here we verify
/// the endpoint semantics that ARE the controller's responsibility.
/// </summary>
public sealed class PersonaSwitchTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _stub = new();
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MaryId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public PersonaSwitchTests()
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

    private async Task SeedUserAndRoleAsync(Guid userId, string email, string fullName, string roleCode)
    {
        await using var db = NewCtx();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode);
        if (role is null)
        {
            role = new Role { Code = roleCode, Name = roleCode };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        var user = new User
        {
            Id = userId,
            Email = email,
            FullName = fullName,
            DisplayName = fullName,
        };
        db.Users.Add(user);
        db.RoleAssignments.Add(new RoleAssignment { RoleId = role.Id, PrincipalId = userId });
        await db.SaveChangesAsync();
    }

    private SandboxController BuildController(out AppDbContext db, Guid actorId, string actorEmail = "admin@example.com")
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
        var controller = new SandboxController(
            new ThrowingSandboxService(), clockService, resetService, mailbox, db, jwt);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(actorId, actorEmail) };
        return controller;
    }

    [Fact]
    public async Task SwitchPersona_when_sandbox_off_returns_400_sandbox_off()
    {
        await SetSandboxAsync(on: false);
        await SeedUserAndRoleAsync(MaryId, "mary@example.com", "Mary", "manager");

        var ctrl = BuildController(out var db, AdminId);
        try
        {
            var result = await ctrl.SwitchPersona(new SwitchPersonaRequest(MaryId), default);
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("sandbox_off", bad.Value!.ToString()!);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task SwitchPersona_when_user_not_found_returns_404()
    {
        await SetSandboxAsync(on: true);

        var ctrl = BuildController(out var db, AdminId);
        try
        {
            var result = await ctrl.SwitchPersona(new SwitchPersonaRequest(Guid.NewGuid()), default);
            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task SwitchPersona_returns_jwt_with_persona_sub_actual_actor_and_sandbox_actor_claims()
    {
        await SetSandboxAsync(on: true);
        await SeedUserAndRoleAsync(MaryId, "mary@example.com", "Mary Manager", "manager");

        var ctrl = BuildController(out var db, AdminId, actorEmail: "admin@bpm.local");
        IActionResult result;
        try
        {
            result = await ctrl.SwitchPersona(new SwitchPersonaRequest(MaryId), default);
        }
        finally { db.Dispose(); }

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal(MaryId.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(AdminId.ToString(), jwt.Claims.First(c => c.Type == "actual_actor_id").Value);
        Assert.Equal("admin@bpm.local", jwt.Claims.First(c => c.Type == "actual_actor_email").Value);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == "sandbox_actor").Value);
        Assert.Contains(jwt.Claims, c => c.Type == "roles" && c.Value == "manager");
        Assert.Equal("mary@example.com", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
    }

    [Fact]
    public async Task SwitchPersona_resolves_multiple_roles_when_persona_has_them()
    {
        await SetSandboxAsync(on: true);
        await SeedUserAndRoleAsync(MaryId, "mary@example.com", "Mary", "manager");
        // Add a second role to Mary
        await using (var db2 = NewCtx())
        {
            var role = new Role { Code = "designer", Name = "designer" };
            db2.Roles.Add(role);
            await db2.SaveChangesAsync();
            db2.RoleAssignments.Add(new RoleAssignment { RoleId = role.Id, PrincipalId = MaryId });
            await db2.SaveChangesAsync();
        }

        var ctrl = BuildController(out var db, AdminId);
        IActionResult result;
        try
        {
            result = await ctrl.SwitchPersona(new SwitchPersonaRequest(MaryId), default);
        }
        finally { db.Dispose(); }

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var token = doc.RootElement.GetProperty("token").GetString();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var roles = jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value).ToList();
        Assert.Contains("manager", roles);
        Assert.Contains("designer", roles);
    }

    private static DefaultHttpContext HttpContextFor(Guid userId, string email)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("roles", "admin"),
        }, authenticationType: "test", nameType: "sub", roleType: "roles");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private sealed class ThrowingSandboxService : ISandboxService
    {
        public Task<SandboxStatusDto> GetStatusAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SandboxStatusDto> SetStatusAsync(UpdateSandboxRequest req, Guid actorUserId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
