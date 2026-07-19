using Bpm.Application.Admin;
using Bpm.Application.Attendance;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Services;
using Bpm.Application.Delegation;
using Bpm.Application.Features.APE.V1;
using Bpm.Application.Features.COMMITTEE_REVIEW.V1;
using Bpm.Application.Features.CONTRACT_REVIEW.V1;
using Bpm.Application.Features.LEAVE.V1;
using Bpm.Application.Features.EOB.V1;
using Bpm.Application.Features.FAD.V1;
using Bpm.Application.Features.FAP.V1;
using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Application.Features.OVERTIME.V1;
using Bpm.Application.Features.ETM.V1;
using Bpm.Application.Features.TEO.V1;
using Bpm.Application.Features.TRQ.V1;
using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Application.Features.WFH.V1;
using Bpm.Application.Features.WFH.V2;
using Bpm.Application.Features.WFH.V3;
using Bpm.Application.Features.WFH.V4;
using Bpm.Application.Features.WFH.V5;
using Bpm.Application.Features.WFH.V6;
using Bpm.Application.Files;
using Bpm.Application.Impersonation;
using Bpm.Application.Inbox;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Application.Sandbox;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Persistence.Admin;
using Bpm.Persistence.Attendance;
using Bpm.Persistence.Common;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.LEAVE.V1;
using Bpm.Persistence.Features.APE.V1;
using Bpm.Persistence.Features.COMMITTEE_REVIEW.V1;
using Bpm.Persistence.Features.CONTRACT_REVIEW.V1;
using Bpm.Persistence.Features.EOB.V1;
using Bpm.Persistence.Features.ETM.V1;
using Bpm.Persistence.Features.FAD.V1;
using Bpm.Persistence.Features.FAP.V1;
using Bpm.Persistence.Features.PURCHASE_REQUEST.V1;
using Bpm.Persistence.Features.OVERTIME.V1;
using Bpm.Persistence.Features.TEO.V1;
using Bpm.Persistence.Features.TRQ.V1;
using Bpm.Persistence.Features.VENDOR_EXPENSE.V1;
using Bpm.Persistence.Features.WFH.V1;
using Bpm.Persistence.Features.WFH.V2;
using Bpm.Persistence.Features.WFH.V3;
using Bpm.Persistence.Features.WFH.V4;
using Bpm.Persistence.Features.WFH.V5;
using Bpm.Persistence.Features.WFH.V6;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Files;
using Bpm.Persistence.Impersonation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
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

        var dbProvider = configuration["Database:Provider"] ?? DbProviderSetup.DefaultProvider;
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=bpm.db";

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            DbProviderSetup.Configure(options, dbProvider, connectionString);
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Org / Spec / Resolver services
        services.AddScoped<IOrgChartReader, OrgChartReader>();
        services.AddScoped<IActorResolutionAuditor, ActorResolutionAuditor>();
        services.AddScoped<IActorResolver, ActorResolver>();
        services.AddSingleton<ActorRefValidator>();
        services.AddScoped<Bpm.Application.Datasets.IDatasetResolutionService, Datasets.DatasetResolutionService>();
        services.AddScoped<Bpm.Application.Parallel.IParallelApprovalService, Parallel.ParallelApprovalService>();

        // IHrFlowService / HrFlowService deleted in Phase 1.3 retirement
        // (RESIGN/DEPTX no longer use the interim controller). Entity
        // classes + tables intentionally retained as inert until the
        // next drop-tables migration so existing seed data doesn't
        // need a destructive migration today.
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<Bpm.Application.Attendance.IAttendanceCorrectionService, Attendance.AttendanceCorrectionService>();
        services.AddScoped<Bpm.Application.Support.ISupportIssueService, Support.SupportIssueService>();
        services.AddScoped<IImpersonationService, ImpersonationService>();
        services.AddScoped<ISandboxService, SandboxService>();
        services.AddScoped<ISandboxClockService, SandboxClockService>();
        services.AddScoped<IOutboundGate, OutboundGate>();
        services.AddScoped<IResetService, ResetService>();
        services.AddScoped<IMailboxService, MailboxService>();
        services.AddScoped<IFlowSandboxConfigService, FlowSandboxConfigService>();
        services.AddScoped<Bpm.Application.Doctor.IDoctorService, Bpm.Persistence.Doctor.DoctorService>();
        services.AddScoped<Bpm.Application.Transfer.ICaseTransferService, Bpm.Persistence.Transfer.CaseTransferService>();
        services.AddScoped<IRoleAdminService, RoleAdminService>();

        // Real delegation lookup over Admin_Delegations (replaces the no-op stub).
        services.AddScoped<IDelegationService, Bpm.Persistence.Delegation.DelegationService>();
        // Shared decision-authorization seam (本人 OR 有效代理人) used by chef flows.
        services.AddScoped<Bpm.Application.Common.Authorization.IActorAuthorizer, Bpm.Application.Common.Authorization.ActorAuthorizer>();

        // Model-B notify fan-out: keep the dev file log AND persist in-app
        // rows for the header bell. Override the Application-layer
        // INotifyDispatcher (FileNotifyDispatcher) with a composite of the
        // two concretes. Construct with explicit instances so the composite
        // never resolves itself via IEnumerable<INotifyDispatcher>.
        // SandboxCaptureNotifyDispatcher is the third sink: when a flow's
        // sandbox capture is effective it records the outbound "email" into
        // SandboxCapturedMessages for the admin Mailbox. Additive — the bell
        // (InApp) and dev log (File) still run.
        services.AddScoped<FileNotifyDispatcher>();
        services.AddScoped<InAppNotifyDispatcher>();
        services.AddScoped<SandboxCaptureNotifyDispatcher>();
        // SmtpNotifyDispatcher is the real outbound email transport (no-ops
        // unless Bpm:Notifications:Smtp:Enabled). AuditNotifyDispatcher writes
        // the production NotificationDispatchAudit row for every send.
        services.AddScoped<SmtpNotifyDispatcher>();
        services.AddScoped<AuditNotifyDispatcher>();
        services.AddScoped<INotifyDispatcher>(sp => new CompositeNotifyDispatcher(
            sp.GetRequiredService<FileNotifyDispatcher>(),
            sp.GetRequiredService<InAppNotifyDispatcher>(),
            sp.GetRequiredService<SandboxCaptureNotifyDispatcher>(),
            sp.GetRequiredService<SmtpNotifyDispatcher>(),
            sp.GetRequiredService<AuditNotifyDispatcher>()));

        // Model-A process runtime (IProcessRuntime / IProcessQueryService /
        // ProcessAdminIntervention / ProcessSimulator / ProcessReporting) was
        // removed — admin Reports now runs on the model-B per-flow case tables
        // via Api/Reports/ReportsController (no analytics service needed).

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

        // Chef-cooked features: per-flow EF case-store impls (the
        // Application-side interfaces are bound to these EF classes here).
        services.AddScoped<ILEAVE_V1_CaseStore, LEAVE_V1_CaseStore>();

        // Lead-owned platform abstraction used by Clean-Arch chef cooks for
        // SharedPrincipal display-name / email / role-member resolution
        // (Application can't reference Persistence directly).
        services.AddScoped<IPrincipalDirectory, PrincipalDirectory>();

        // Per-flow data-access ports for Clean-Arch chef cooks. Each cook
        // ships its store interface in Application/Features/<CODE>/V<N>/
        // and the EF impl in Persistence/Features/<CODE>/V<N>/, then wires
        // the binding here so DI can hand the impl to Application services.
        services.AddScoped<IPURCHASE_REQUEST_V1_CaseStore, PURCHASE_REQUEST_V1_CaseStore>();
        services.AddScoped<IOVERTIME_V1_CaseStore, OVERTIME_V1_CaseStore>();
        services.AddScoped<ITRQ_V1_CaseStore, TRQ_V1_CaseStore>();
        services.AddScoped<IAPE_V1_CaseStore, APE_V1_CaseStore>();
        services.AddScoped<ICONTRACT_REVIEW_V1_CaseStore, CONTRACT_REVIEW_V1_CaseStore>();
        services.AddScoped<ICOMMITTEE_REVIEW_V1_CaseStore, COMMITTEE_REVIEW_V1_CaseStore>();
        services.AddScoped<ITEO_V1_CaseStore, TEO_V1_CaseStore>();
        services.AddScoped<IFAP_V1_CaseStore, FAP_V1_CaseStore>();
        services.AddScoped<IFAD_V1_CaseStore, FAD_V1_CaseStore>();
        services.AddScoped<IEOB_V1_CaseStore, EOB_V1_CaseStore>();
        services.AddScoped<IETM_V1_CaseStore, ETM_V1_CaseStore>();
        services.AddScoped<IVENDOR_EXPENSE_V1_CaseStore, VENDOR_EXPENSE_V1_CaseStore>();
        services.AddScoped<IWFH_V1_CaseStore, WFH_V1_CaseStore>();
        services.AddScoped<IWFH_V2_CaseStore, WFH_V2_CaseStore>();
        services.AddScoped<IWFH_V3_CaseStore, WFH_V3_CaseStore>();
        services.AddScoped<IWFH_V4_CaseStore, WFH_V4_CaseStore>();
        services.AddScoped<IWFH_V5_CaseStore, WFH_V5_CaseStore>();
        services.AddScoped<IWFH_V6_CaseStore, WFH_V6_CaseStore>();

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
