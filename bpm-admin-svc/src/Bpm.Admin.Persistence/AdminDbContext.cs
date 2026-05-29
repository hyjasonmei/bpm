using System.Linq.Expressions;
using Bpm.Admin.Domain.Audit;
using Bpm.Admin.Domain.Auth;
using Bpm.Admin.Domain.Delegations;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Domain.Roles;
using Bpm.Admin.Domain.SoftDelete;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence;

public class AdminDbContext : DbContext
{
    public AdminDbContext(DbContextOptions<AdminDbContext> options) : base(options)
    {
    }

    public DbSet<Principal> Principals => Set<Principal>();
    public DbSet<UserDept> UserDepts => Set<UserDept>();
    public DbSet<UserManager> UserManagers => Set<UserManager>();
    public DbSet<DeptParent> DeptParents => Set<DeptParent>();
    public DbSet<DeptHead> DeptHeads => Set<DeptHead>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<PrincipalRole> PrincipalRoles => Set<PrincipalRole>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<FlowChatMessage> FlowChatMessages => Set<FlowChatMessage>();
    public DbSet<FlowGroup> FlowGroups => Set<FlowGroup>();
    public DbSet<FeatureRegistration> FeatureRegistrations => Set<FeatureRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdminDbContext).Assembly);

        ApplySoftDeleteQueryFilter(modelBuilder);
        ApplyAdminTablePrefix(modelBuilder);
    }

    /// <summary>
    /// Every admin table gets an `Admin_` prefix so it never collides
    /// with bpm-svc's tables when both DbContexts target the same
    /// SQLite file (post-db-merge per flowcook-mvp Phase 1.1).
    /// bpm-svc has its own <c>Principals</c>, <c>Roles</c>, etc. with
    /// a different schema — without this rename the two contexts
    /// would silently stomp on each other.
    /// </summary>
    private static void ApplyAdminTablePrefix(ModelBuilder modelBuilder)
    {
        const string prefix = "Admin_";
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue;
            if (tableName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            entityType.SetTableName(prefix + tableName);
        }
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        EnforceAuditAppendOnly();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        EnforceAuditAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Refuses any Modified or Deleted state on AuditEvent. Per
    /// flowcook-audit spec, audit rows are append-only forever; corrections
    /// are issued as new events with action_type = correction.
    /// </summary>
    private void EnforceAuditAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AuditEvent rows are append-only. Issue a new event with action_type='correction' " +
                    "referencing the original event id rather than mutating the existing row.");
            }
        }
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case Principal principal:
                    if (entry.State == EntityState.Added)
                    {
                        if (principal.CreatedAt == default) principal.CreatedAt = now;
                        if (principal.UpdatedAt == default) principal.UpdatedAt = now;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        principal.UpdatedAt = now;
                    }
                    break;

                case Delegation delegation:
                    if (entry.State == EntityState.Added)
                    {
                        if (delegation.CreatedAt == default) delegation.CreatedAt = now;
                        if (delegation.UpdatedAt == default) delegation.UpdatedAt = now;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        delegation.UpdatedAt = now;
                    }
                    break;

                case PrincipalRole pr:
                    if (entry.State == EntityState.Added && pr.AssignedAt == default)
                    {
                        pr.AssignedAt = now;
                    }
                    break;

                case Flow flow:
                    if (entry.State == EntityState.Added)
                    {
                        if (flow.CreatedAt == default) flow.CreatedAt = now;
                        if (flow.UpdatedAt == default) flow.UpdatedAt = now;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        flow.UpdatedAt = now;
                    }
                    break;
            }
        }
    }

    private static void ApplySoftDeleteQueryFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var deletedAt = Expression.Property(parameter, nameof(ISoftDeletable.DeletedAt));
                var nullConstant = Expression.Constant(null, typeof(DateTime?));
                var filter = Expression.Lambda(Expression.Equal(deletedAt, nullConstant), parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }
}
