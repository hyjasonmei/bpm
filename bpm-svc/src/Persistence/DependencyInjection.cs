using Bpm.Application.Admin;
using Bpm.Application.Attendance;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Services;
using Bpm.Application.Delegation;
using Bpm.Application.Impersonation;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Application.Process.Admin;
using Bpm.Application.Process.Reporting;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Application.Process.Simulator;
using Bpm.Application.Sandbox;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Persistence.Admin;
using Bpm.Persistence.Attendance;
using Bpm.Persistence.Common;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Impersonation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Process.Admin;
using Bpm.Persistence.Process.Reporting;
using Bpm.Persistence.Process.Simulator;
using Bpm.Persistence.Sandbox;
using Bpm.Persistence.Spec;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bpm.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // PR-J3: IClock now resolves through SandboxClock (Scoped) so sandbox
        // mode can shift time for every downstream consumer. SystemClock stays
        // available as a Singleton for code that explicitly wants real wall-clock
        // time (e.g., SandboxClockService.GetAsync's "realNow" output).
        services.AddSingleton<SystemClock>();
        services.AddScoped<IClock, SandboxClock>();
        services.AddScoped<IScheduledJobKicker, NoOpScheduledJobKicker>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        // Bypass SandboxClock decorator here — SandboxClock depends on
        // AppDbContext, and AppDbContext resolves this interceptor via its
        // options factory, which would form a construction-time cycle.
        // Audit timestamps use wall-clock; sandbox-shifted time still applies
        // to app-level CreatedAt that goes through IClock at the service layer.
        services.AddScoped<AuditSaveChangesInterceptor>(sp =>
            new AuditSaveChangesInterceptor(
                sp.GetRequiredService<SystemClock>(),
                sp.GetRequiredService<ICurrentUser>(),
                sp.GetService<ISandboxActorContext>()));

        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=bpm.db";
        connectionString = DbPathResolver.Normalize(connectionString);

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Org / Spec / Resolver services
        services.AddScoped<IOrgChartReader, OrgChartReader>();
        services.AddScoped<IActorResolutionAuditor, ActorResolutionAuditor>();
        services.AddScoped<IActorResolver, ActorResolver>();
        services.AddSingleton<ActorRefValidator>();

        // IHrFlowService / HrFlowService deleted in Phase 1.3 retirement
        // (RESIGN/DEPTX no longer use the interim controller). Entity
        // classes + tables intentionally retained as inert until the
        // next drop-tables migration so existing seed data doesn't
        // need a destructive migration today.
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IImpersonationService, ImpersonationService>();
        services.AddScoped<ISandboxService, SandboxService>();
        services.AddScoped<ISandboxClockService, SandboxClockService>();
        services.AddScoped<IOutboundGate, OutboundGate>();
        services.AddScoped<IResetService, ResetService>();
        services.AddScoped<IMailboxService, MailboxService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();

        // Process runtime + collaborator stubs (Delegation / Notifications /
        // SpecLoader land here so the runtime can compose against real seams).
        services.AddScoped<ISpecLoader, FileSystemSpecLoader>();
        services.AddScoped<IDelegationService, StubDelegationService>();
        // PR-J6 §11.6: SandboxCapturingNotificationDispatcher writes to
        // SandboxCapturedMessages when sandbox is on, falls through to logging
        // when off. Replaces LoggingNotificationDispatcher as the production
        // wiring; the logging class is kept around for tests / other callers
        // that want the no-side-effects stub.
        services.AddScoped<INotificationDispatcher, SandboxCapturingNotificationDispatcher>();
        services.AddScoped<IProcessRuntime, ProcessRuntime>();
        services.AddScoped<IProcessQueryService, ProcessQueryService>();

        // PR-K4: Process Admin intervention surface — sits beside the runtime
        // and reuses the same DbContext so admin overrides land in the same
        // EF unit-of-work as the regular runtime mutations they trigger.
        services.AddScoped<IProcessAdminInterventionService, ProcessAdminInterventionService>();

        // PR-K3: Process Simulator — drives the live runtime against a chosen
        // flow code with delete-on-finally cleanup so simulation leaves no
        // rows behind in any of the runtime tables.
        services.AddScoped<IProcessSimulator, ProcessSimulator>();

        // PR-K5: Reporting service. The cached wrapper sits in front of the
        // raw aggregator with a 5-min TTL, keyed by tenant + spec + period.
        // Memory cache is process-local — fine for the in-process runtime
        // we ship today; once the API runs as multiple instances the cache
        // becomes per-replica and stale-by-TTL is the (acceptable) outcome.
        services.AddMemoryCache();
        services.AddScoped<ProcessReportingService>();
        services.AddScoped<IProcessReportingService>(sp => new CachedProcessReportingService(
            (IProcessReportingService)sp.GetRequiredService<ProcessReportingService>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));

        return services;
    }
}
