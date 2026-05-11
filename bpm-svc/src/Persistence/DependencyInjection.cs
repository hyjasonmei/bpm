using Bpm.Application.Admin;
using Bpm.Application.Attendance;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Services;
using Bpm.Application.Delegation;
using Bpm.Application.HrFlows;
using Bpm.Application.Impersonation;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Application.Sandbox;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Persistence.Admin;
using Bpm.Persistence.Attendance;
using Bpm.Persistence.Common;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.HrFlows;
using Bpm.Persistence.Impersonation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Sandbox;
using Bpm.Persistence.Spec;
using Bpm.Persistence.Spec.Bundle;
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

        services.AddScoped<AuditSaveChangesInterceptor>();

        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=bpm.db";

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

        services.AddScoped<IHrFlowService, HrFlowService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IImpersonationService, ImpersonationService>();
        services.AddScoped<ISandboxService, SandboxService>();
        services.AddScoped<ISandboxClockService, SandboxClockService>();
        services.AddScoped<IOutboundGate, OutboundGate>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();

        // Process runtime + collaborator stubs (Delegation / Notifications /
        // SpecLoader land here so the runtime can compose against real seams).
        services.AddScoped<ISpecLoader, FileSystemSpecLoader>();
        services.AddScoped<IDelegationService, StubDelegationService>();
        services.AddScoped<INotificationDispatcher, LoggingNotificationDispatcher>();
        services.AddScoped<IProcessRuntime, ProcessRuntime>();
        services.AddScoped<IProcessQueryService, ProcessQueryService>();

        // Spec Bundle runtime (PR-I4): scratch-tenant seeder + replay runner.
        services.AddScoped<IBundleRuntimeLoader, BundleRuntimeLoader>();
        services.AddScoped<IBundleReproducibilityRunner, BundleReproducibilityRunner>();

        return services;
    }
}
