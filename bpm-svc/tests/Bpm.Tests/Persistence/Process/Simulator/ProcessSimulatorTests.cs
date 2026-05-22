using System.Text.Json;
using Bpm.Application.Org;
using Bpm.Application.Process.Simulator;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Process.Simulator;
using Bpm.Persistence.Sandbox;
using Bpm.Persistence.Spec;
using Bpm.Tests.Common;
using Bpm.Tests.Persistence.Spec.Bundle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Persistence.Process.Simulator;

/// <summary>
/// PR-K3 §4.2 — process simulator end-to-end. Builds an in-memory SQLite,
/// seeds a small org, points <c>Bpm:SampleSpecsDir</c> at the repo's
/// <c>sample_specs/</c> tree (so the FileSystemSpecLoader hits the real
/// LEAVE spec), and exercises the simulator's drive-then-cleanup loop.
/// </summary>
public sealed class ProcessSimulatorTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private readonly string _sampleSpecsDir = "/Users/jason/claude/bpm/sample_specs";

    private Guid _aliceId;
    private Guid _bobId;
    private Guid _vpUserId;

    public ProcessSimulatorTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
        SeedMinimalOrg(seed);
    }

    public void Dispose() => _conn.Dispose();

    /* ────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task LEAVE_happy_path_returns_success_with_expected_trace()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3}");

        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));

        Assert.True(result.Success, $"expected success, got error: {result.Error}");
        Assert.Equal("Completed", result.FinalStatus);
        var nodeIds = result.Trace.Select(s => s.NodeId).ToList();
        Assert.Contains("task_apply", nodeIds);
        Assert.Contains("approval_manager", nodeIds);
        Assert.Contains("task_hr_archive", nodeIds);
    }

    [Fact]
    public async Task LEAVE_8_day_path_routes_through_VP_approval()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"事假\",\"days\":8}");

        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));

        Assert.True(result.Success, $"expected success, got error: {result.Error}");
        Assert.Equal("Completed", result.FinalStatus);
        var nodeIds = result.Trace.Select(s => s.NodeId).ToList();
        Assert.Contains("approval_vp", nodeIds);
    }

    [Fact]
    public async Task Unknown_flowCode_returns_failure_with_not_found_error()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{}");

        var result = await sim.SimulateAsync(new SimulationRequest("DOES_NOT_EXIST", doc.RootElement.Clone()));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Empty_flowCode_returns_failure_without_touching_loader()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{}");

        var result = await sim.SimulateAsync(new SimulationRequest("", doc.RootElement.Clone()));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Simulation_persists_no_rows_to_runtime_tables()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3}");

        var instancesBefore = await CountAsync(db => db.ProcessInstances.CountAsync());
        var tasksBefore = await CountAsync(db => db.ProcessTasks.CountAsync());
        var historyBefore = await CountAsync(db => db.TaskHistory.CountAsync());
        var msgsBefore = await CountAsync(db => db.SandboxCapturedMessages.CountAsync());

        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));
        Assert.True(result.Success, $"expected success, got error: {result.Error}");

        var instancesAfter = await CountAsync(db => db.ProcessInstances.CountAsync());
        var tasksAfter = await CountAsync(db => db.ProcessTasks.CountAsync());
        var historyAfter = await CountAsync(db => db.TaskHistory.CountAsync());
        var msgsAfter = await CountAsync(db => db.SandboxCapturedMessages.CountAsync());

        Assert.Equal(instancesBefore, instancesAfter);
        Assert.Equal(tasksBefore, tasksAfter);
        Assert.Equal(historyBefore, historyAfter);
        Assert.Equal(msgsBefore, msgsAfter);
    }

    [Fact]
    public async Task Sandbox_state_is_restored_after_simulate()
    {
        // Caller never opted into sandbox — the simulator flips it on for
        // capture, but the prior "off / no row" state must be restored so
        // we don't accidentally leave the host in sandbox.
        Assert.Null(await GetTenantSettingsAsync());

        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3}");
        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));
        Assert.True(result.Success);

        var after = await GetTenantSettingsAsync();
        // Either the row was re-created with SandboxMode=false, or the
        // simulator left no row (acceptable: pre-call state was no-row).
        if (after is not null) Assert.False(after.SandboxMode);
    }

    [Fact]
    public async Task Sandbox_state_is_restored_when_caller_was_already_in_sandbox()
    {
        await SetSandboxAsync(true);

        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3}");
        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));
        Assert.True(result.Success);

        var after = await GetTenantSettingsAsync();
        Assert.NotNull(after);
        Assert.True(after!.SandboxMode);
    }

    [Fact]
    public async Task Notifications_are_captured_in_result_when_caller_was_not_in_sandbox()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3}");

        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));

        Assert.True(result.Success);
        // LEAVE has notify_assign_manager (on_assign) + notify_complete (on_complete).
        Assert.NotEmpty(result.Notifications);
        Assert.Contains(result.Notifications, n => n.NotificationId == "notify_assign_manager");
    }

    [Fact]
    public async Task Initiator_override_picks_specified_user()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3}");

        var result = await sim.SimulateAsync(new SimulationRequest(
            "LEAVE", doc.RootElement.Clone(), SampleOrg: null, InitiatorUserId: _bobId));

        Assert.True(result.Success, $"expected success, got error: {result.Error}");
        var firstAssignee = result.Trace.FirstOrDefault(s => s.NodeId == "task_apply")?.ResolvedAssigneeUserId;
        Assert.Equal(_bobId, firstAssignee);
    }

    [Fact]
    public async Task Final_form_data_reflects_input_form()
    {
        var sim = BuildSimulator();
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\",\"days\":3,\"reason\":\"family\"}");

        var result = await sim.SimulateAsync(new SimulationRequest("LEAVE", doc.RootElement.Clone()));

        Assert.True(result.Success, $"expected success, got error: {result.Error}");
        Assert.NotNull(result.FinalFormData);
        Assert.Equal("特休", result.FinalFormData!.Value.GetProperty("leave_type").GetString());
        Assert.Equal("family", result.FinalFormData.Value.GetProperty("reason").GetString());
    }

    /* ────────────────────────────────────────────────────────────── */

    private ProcessSimulator BuildSimulator()
    {
        var simDb = new AppDbContext(_options);
        var runtimeDb = new AppDbContext(_options);
        var loader = new FileSystemSpecLoader(BuildConfig(), NullLogger<FileSystemSpecLoader>.Instance);

        var orgReader = new OrgChartReader(runtimeDb);
        var auditor = new NoOpResolutionAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var gate = new OutboundGate(runtimeDb, _clock);
        var dispatcher = new SandboxCapturingNotificationDispatcher(
            gate, runtimeDb, NullLogger<SandboxCapturingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var runtime = new ProcessRuntime(runtimeDb, _clock, loader, resolver, delegation, dispatcher,
            evaluator, NullLogger<ProcessRuntime>.Instance);

        return new ProcessSimulator(simDb, runtime, loader, NullLogger<ProcessSimulator>.Instance);
    }

    private IConfiguration BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bpm:SampleSpecsDir"] = _sampleSpecsDir,
        })
        .Build();

    private async Task<int> CountAsync(Func<AppDbContext, Task<int>> q)
    {
        await using var db = new AppDbContext(_options);
        return await q(db);
    }

    private async Task<TenantSettings?> GetTenantSettingsAsync()
    {
        await using var db = new AppDbContext(_options);
        return await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantCode == "default");
    }

    private async Task SetSandboxAsync(bool enabled)
    {
        await using var db = new AppDbContext(_options);
        var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantCode == "default");
        if (row is null)
        {
            row = new TenantSettings { TenantCode = "default" };
            db.TenantSettings.Add(row);
        }
        row.SandboxMode = enabled;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Minimal org seed: an Alice (manager) + Bob (reports to Alice) pair,
    /// plus a VP user with an HR group + role for the LEAVE spec's role
    /// resolution paths. Enough to drive the LEAVE flow without invoking
    /// the full OrgFixture (which would seed unrelated personas).
    /// </summary>
    private void SeedMinimalOrg(AppDbContext db)
    {
        var alice = new User
        {
            Id = Guid.NewGuid(), Email = "alice@simtest.local",
            FullName = "Alice Manager", DisplayName = "Alice", IsActive = true,
        };
        var bob = new User
        {
            Id = Guid.NewGuid(), Email = "bob@simtest.local",
            FullName = "Bob Employee", DisplayName = "Bob", IsActive = true,
            ManagerId = alice.Id,
        };
        var vp = new User
        {
            Id = Guid.NewGuid(), Email = "vp@simtest.local",
            FullName = "Vince VP", DisplayName = "Vince", IsActive = true,
        };
        db.Users.AddRange(alice, bob, vp);

        var hrGroup = new Group
        {
            Id = Guid.NewGuid(), Code = "HR", Name = "Human Resources",
            DisplayName = "HR",
        };
        db.Groups.Add(hrGroup);
        db.GroupMembers.Add(new GroupMember { GroupId = hrGroup.Id, PrincipalId = alice.Id });

        var vpRole = new Role
        {
            Id = Guid.NewGuid(), Code = "VP", Name = "Vice President",
            Scope = RoleScope.System,
        };
        var hrRole = new Role
        {
            Id = Guid.NewGuid(), Code = "HR", Name = "Human Resources",
            Scope = RoleScope.System,
        };
        db.Roles.AddRange(vpRole, hrRole);
        db.RoleAssignments.AddRange(
            new RoleAssignment { Id = Guid.NewGuid(), RoleId = vpRole.Id, PrincipalId = vp.Id },
            new RoleAssignment { Id = Guid.NewGuid(), RoleId = hrRole.Id, PrincipalId = alice.Id });

        db.SaveChanges();

        _aliceId = alice.Id;
        _bobId = bob.Id;
        _vpUserId = vp.Id;
    }
}
