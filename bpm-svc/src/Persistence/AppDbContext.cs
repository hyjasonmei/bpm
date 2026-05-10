using System.Reflection;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Attendance;
using Bpm.Domain.Entities.Audit;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.HrFlows;
using Bpm.Domain.Entities.Impersonation;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Sandbox;
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
    public DbSet<SandboxRedirect> SandboxRedirects => Set<SandboxRedirect>();

    public DbSet<RoleAssignmentChange> RoleAssignmentChanges => Set<RoleAssignmentChange>();

    public DbSet<ProcessInstance> ProcessInstances => Set<ProcessInstance>();
    public DbSet<ProcessTask> ProcessTasks => Set<ProcessTask>();
    public DbSet<TaskHistory> TaskHistory => Set<TaskHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);
    }
}
