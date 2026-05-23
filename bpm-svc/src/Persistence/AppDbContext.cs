using System.Reflection;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Attendance;
using Bpm.Domain.Entities.Audit;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.HrFlows;
using Bpm.Domain.Entities.Impersonation;
using Bpm.Domain.Entities.Notifications;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Principal> Principals => Set<Principal>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

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

    // Shared identity — mappings onto admin-svc's Admin_* tables. bpm-svc
    // doesn't own these schemas; admin-svc runs the migrations. Added by
    // openspec/changes/unify-user-store-and-real-auth. The legacy bpm-local
    // identity DbSets above stay live during the U1→U2 transition and get
    // removed in the DropBpmIdentityTables migration once consumers swap over.
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
