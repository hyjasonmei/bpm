using System.Security.Claims;
using Bpm.Api.Process;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Spec;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bpm.Tests.Api.Process;

/// <summary>
/// Shared fixture for controller-layer integration tests. We deliberately
/// skip <c>WebApplicationFactory&lt;Program&gt;</c> — Program.cs runs DB
/// migrations + seeding at startup, which collides with the in-memory SQLite
/// each test class spins up. Direct controller construction with a real
/// <see cref="ProcessRuntime"/> + <see cref="ProcessQueryService"/> is enough
/// to exercise routing decisions, the auth claim flow, and runtime wiring.
/// </summary>
internal sealed class ProcessTestFixture : IDisposable
{
    public SqliteConnection Conn { get; }
    public DbContextOptions<AppDbContext> Options { get; }
    public StubClock Clock { get; }
    public Guid WilsonId { get; } = Guid.NewGuid();
    public Guid EltonId { get; } = Guid.NewGuid();
    public Guid AmyId { get; } = Guid.NewGuid();
    public Guid CeoId { get; } = Guid.NewGuid();

    public ProcessTestFixture()
    {
        Clock = new StubClock();
        Conn = new SqliteConnection("DataSource=:memory:");
        Conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(Clock, new StubCurrentUser());
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(Conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(Options);
        seed.Database.EnsureCreated();
        SeedOrg(seed);
    }

    public void Dispose() => Conn.Dispose();

    private void SeedOrg(AppDbContext db)
    {
        var corp = new Department { Id = Guid.NewGuid(), Code = "CORP", Name = "Corporate", DisplayName = "Corporate" };
        var eng = new Department { Id = Guid.NewGuid(), Code = "ENG", Name = "Engineering", DisplayName = "Engineering", ParentId = corp.Id };

        var ceo = new User { Id = CeoId, DisplayName = "CEO", Email = "ceo@x", FullName = "CEO", DepartmentId = corp.Id };
        var elton = new User { Id = EltonId, DisplayName = "Elton", Email = "manager@x", FullName = "Elton Yang", DepartmentId = eng.Id, ManagerId = ceo.Id };
        var wilson = new User { Id = WilsonId, DisplayName = "Wilson", Email = "employee@x", FullName = "Wilson You", DepartmentId = eng.Id, ManagerId = elton.Id };
        var amy = new User { Id = AmyId, DisplayName = "Amy", Email = "hr@x", FullName = "Amy Lin (HR)", DepartmentId = corp.Id, ManagerId = ceo.Id };

        db.Departments.AddRange(corp, eng);
        db.Users.AddRange(ceo, elton, wilson, amy);

        var hrRole = new Role { Id = Guid.NewGuid(), Code = "HR", Name = "Human Resources", Scope = RoleScope.System };
        var vpRole = new Role { Id = Guid.NewGuid(), Code = "VP", Name = "Vice President", Scope = RoleScope.System };
        db.Roles.AddRange(hrRole, vpRole);
        db.SaveChanges();

        corp.HeadUserId = ceo.Id;
        eng.HeadUserId = elton.Id;
        db.RoleAssignments.Add(new RoleAssignment { RoleId = hrRole.Id, PrincipalId = amy.Id });
        db.RoleAssignments.Add(new RoleAssignment { RoleId = vpRole.Id, PrincipalId = ceo.Id });
        db.SaveChanges();
    }

    /// <summary>
    /// Build a fresh DbContext + the runtime/query stack pointed at it. Each
    /// caller gets its own DbContext so we can simulate independent requests.
    /// </summary>
    public (AppDbContext db, ProcessRuntime runtime, ProcessQueryService query) BuildStack()
    {
        var db = new AppDbContext(Options);
        var orgReader = new OrgChartReader(db);
        var auditor = new NoOpAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var notifier = new LoggingNotificationDispatcher(NullLogger<LoggingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(Clock);

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bpm:SampleSpecsDir"] = "/Users/jason/claude/bpm/sample_specs",
            })
            .Build();
        var loader = new FileSystemSpecLoader(cfg, NullLogger<FileSystemSpecLoader>.Instance);

        var runtime = new ProcessRuntime(db, Clock, loader, resolver, delegation, notifier, evaluator,
            NullLogger<ProcessRuntime>.Instance);
        var query = new ProcessQueryService(db);
        return (db, runtime, query);
    }

    public ProcessController BuildProcessController(
        IProcessRuntime runtime,
        IProcessQueryService query,
        Guid actingUserId)
    {
        var controller = new ProcessController(runtime, query);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(actingUserId) };
        return controller;
    }

    public TaskController BuildTaskController(
        IProcessRuntime runtime,
        IProcessQueryService query,
        Guid actingUserId)
    {
        var controller = new TaskController(runtime, query);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(actingUserId) };
        return controller;
    }

    private static DefaultHttpContext HttpContextFor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
        }, authenticationType: "test", nameType: "sub", roleType: "roles");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}

/// <summary>No-op resolver auditor for tests — production wiring writes
/// resolution decisions into <c>actor_resolution_audit</c>.</summary>
internal sealed class NoOpAuditor : IActorResolutionAuditor
{
    public Task WriteAsync(Bpm.Domain.Spec.ActorRef actor, Bpm.Domain.Spec.ResolutionContext ctx, Bpm.Domain.Spec.ResolutionResult result, CancellationToken ct = default)
        => Task.CompletedTask;
}
