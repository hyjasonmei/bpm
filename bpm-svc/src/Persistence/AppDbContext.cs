using System.Reflection;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Attendance;
using Bpm.Domain.Entities.Audit;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Files;
using Bpm.Domain.Entities.HrFlows;
using Bpm.Domain.Entities.Impersonation;
using Bpm.Domain.Entities.Notifications;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Domain.Entities.Spec;
using Bpm.Domain.Features.LEAVE.V1;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    // Identity lives in admin-svc post unify-user-store; bpm-svc reads
    // through the SharedX DbSets below, which target the Admin_* tables.

    public DbSet<ActorResolutionAudit> ActorResolutionAudits => Set<ActorResolutionAudit>();

    public DbSet<HrFlowInstance> HrFlowInstances => Set<HrFlowInstance>();
    public DbSet<HrFlowAction> HrFlowActions => Set<HrFlowAction>();

    public DbSet<AttendancePunch> AttendancePunches => Set<AttendancePunch>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();

    public DbSet<Bpm.Domain.Entities.Support.SupportIssue> SupportIssues => Set<Bpm.Domain.Entities.Support.SupportIssue>();

    public DbSet<ImpersonationSession> ImpersonationSessions => Set<ImpersonationSession>();

    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<SandboxCapturedMessage> SandboxCapturedMessages => Set<SandboxCapturedMessage>();
    public DbSet<FlowSandboxConfig> FlowSandboxConfigs => Set<FlowSandboxConfig>();
    public DbSet<Bpm.Domain.Entities.Doctor.DoctorActionLog> DoctorActionLogs => Set<Bpm.Domain.Entities.Doctor.DoctorActionLog>();
    public DbSet<Bpm.Domain.Entities.Transfer.CaseTransferLog> CaseTransferLogs => Set<Bpm.Domain.Entities.Transfer.CaseTransferLog>();

    public DbSet<RoleAssignmentChange> RoleAssignmentChanges => Set<RoleAssignmentChange>();

    // Shared parallel-approval primitive (並簽): one group per parallel gateway
    // instance, N slots per group. Used by any flow whose spec has a parallel gateway.
    public DbSet<Bpm.Domain.Parallel.ParallelApprovalGroup> ParallelApprovalGroups => Set<Bpm.Domain.Parallel.ParallelApprovalGroup>();
    public DbSet<Bpm.Domain.Parallel.ParallelApprovalSlot> ParallelApprovalSlots => Set<Bpm.Domain.Parallel.ParallelApprovalSlot>();

    public DbSet<SpecBundle> SpecBundles => Set<SpecBundle>();

    public DbSet<NotificationDispatchAudit> NotificationDispatchAudits => Set<NotificationDispatchAudit>();

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<FileBlob> FileBlobs => Set<FileBlob>();

    // Chef-cooked features — one DbSet per per-flow entity.
    public DbSet<LEAVE_V1_Case> LEAVE_V1_Cases => Set<LEAVE_V1_Case>();

    // Shared identity — admin-svc owns the Admin_* schemas; bpm-svc reads
    // through these mappings with ExcludeFromMigrations so it never tries
    // to CreateTable / DropTable them.
    public DbSet<SharedPrincipal> SharedPrincipals => Set<SharedPrincipal>();
    public DbSet<SharedUserCredential> SharedUserCredentials => Set<SharedUserCredential>();
    public DbSet<SharedRole> SharedRoles => Set<SharedRole>();
    public DbSet<SharedPrincipalRole> SharedPrincipalRoles => Set<SharedPrincipalRole>();
    public DbSet<SharedUserDept> SharedUserDepts => Set<SharedUserDept>();
    public DbSet<SharedUserManager> SharedUserManagers => Set<SharedUserManager>();
    public DbSet<SharedDeptParent> SharedDeptParents => Set<SharedDeptParent>();
    public DbSet<SharedDeptHead> SharedDeptHeads => Set<SharedDeptHead>();
    public DbSet<SharedGroupMember> SharedGroupMembers => Set<SharedGroupMember>();
    /// <summary>Writable mirror onto Admin_Delegations — self-service delegation (see SharedDelegation).</summary>
    public DbSet<SharedDelegation> SharedDelegations => Set<SharedDelegation>();
    /// <summary>Read-only flow-registry view onto Admin_Flows — see SharedFlow.</summary>
    public DbSet<SharedFlow> SharedFlows => Set<SharedFlow>();
    /// <summary>Read-only launcher-group view onto Admin_FlowGroups — see SharedFlowGroup.</summary>
    public DbSet<SharedFlowGroup> SharedFlowGroups => Set<SharedFlowGroup>();
    /// <summary>Read-only view onto admin-owned custom datasets — see SharedDataset.</summary>
    public DbSet<SharedDataset> SharedDatasets => Set<SharedDataset>();
    public DbSet<SharedDatasetRow> SharedDatasetRows => Set<SharedDatasetRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);
    }
}
