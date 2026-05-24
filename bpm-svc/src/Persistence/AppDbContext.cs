using System.Reflection;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Attendance;
using Bpm.Domain.Entities.Audit;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.HrFlows;
using Bpm.Domain.Entities.Impersonation;
using Bpm.Domain.Entities.Notifications;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Domain.Entities.Spec;
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

    public DbSet<ImpersonationSession> ImpersonationSessions => Set<ImpersonationSession>();

    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<SandboxCapturedMessage> SandboxCapturedMessages => Set<SandboxCapturedMessage>();

    public DbSet<RoleAssignmentChange> RoleAssignmentChanges => Set<RoleAssignmentChange>();

    public DbSet<ProcessInstance> ProcessInstances => Set<ProcessInstance>();
    public DbSet<ProcessTask> ProcessTasks => Set<ProcessTask>();
    public DbSet<TaskHistory> TaskHistory => Set<TaskHistory>();

    public DbSet<SpecBundle> SpecBundles => Set<SpecBundle>();

    public DbSet<NotificationDispatchAudit> NotificationDispatchAudits => Set<NotificationDispatchAudit>();

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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);
    }
}
