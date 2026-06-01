using Bpm.Application.Admin;
using Bpm.Application.Attendance;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Services;
using Bpm.Application.Delegation;
using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Application.Files;
using Bpm.Application.Impersonation;
using Bpm.Application.Inbox;
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
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.LEAVE.V1;
using Bpm.Persistence.Features.PURCHASE_REQUEST.V1;
using Bpm.Persistence.Features.VENDOR_EXPENSE.V1;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Files;
using Bpm.Persistence.Impersonation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Process.Admin;
using Bpm.Persistence.Process.Reporting;
using Bpm.Persistence.Process.Simulator;
using Bpm.Persistence.Sandbox;
using Bpm.Application.Auth;
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

        // Real password login (unify-user-store): verifies hashes against
        // Admin_UserCredentials with the same ASP.NET Identity PasswordHasher
        // admin-svc seeded with.
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // Core file-storage primitive — chef-cooked features consume this
        // via the FilePicker UI primitive (POST /api/files) instead of
        // rolling their own per-feature upload paths. Bind by hand so this
        // csproj doesn't need to pull in Configuration.Binder just for one
        // options record.
        var fileOptions = new FileStorageOptions();
        var fileSection = configuration.GetSection("Files");
        if (long.TryParse(fileSection["MaxBytes"], out var maxBytes) && maxBytes > 0)
            fileOptions.MaxBytes = maxBytes;
        var rootPath = fileSection["RootPath"];
        if (!string.IsNullOrWhiteSpace(rootPath))
            fileOptions.RootPath = rootPath;
        services.AddSingleton(fileOptions);
        services.AddScoped<IFileStorageService, FileStorageService>();

        // Chef-cooked features: per-flow state-machine services.
        // LEAVE V1 lives entirely in Persistence under the old (pre-Clean-Arch)
        // shape, so its service is registered here. New (Clean-Arch) cooks
        // register their service from Application/DI instead — see
        // PURCHASE_REQUEST V1 for the canonical pattern.
        services.AddScoped<LEAVE_V1_LeaveService>();

        // Lead-owned platform abstraction used by Clean-Arch chef cooks for
        // SharedPrincipal display-name / email / role-member resolution
        // (Application can't reference Persistence directly).
        services.AddScoped<IPrincipalDirectory, PrincipalDirectory>();

        // Per-flow data-access ports for Clean-Arch chef cooks. Each cook
        // ships its store interface in Application/Features/<CODE>/V<N>/
        // and the EF impl in Persistence/Features/<CODE>/V<N>/, then wires
        // the binding here so DI can hand the impl to Application services.
        services.AddScoped<IPURCHASE_REQUEST_V1_CaseStore, PURCHASE_REQUEST_V1_CaseStore>();
        services.AddScoped<IVENDOR_EXPENSE_V1_CaseStore, VENDOR_EXPENSE_V1_CaseStore>();

        // Unified inbox: scan this assembly for ITypedInboxProvider
        // implementations and register each one. Chef-cooked flows
        // drop `<CODE>_V<N>_InboxProvider.cs` into
        // `Persistence/Features/<CODE>/V<N>/` and InboxController
        // picks them up automatically.
        foreach (var providerType in typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                        && !t.IsInterface
                        && typeof(ITypedInboxProvider).IsAssignableFrom(t)))
        {
            services.AddScoped(typeof(ITypedInboxProvider), providerType);
        }

        return services;
    }
}
