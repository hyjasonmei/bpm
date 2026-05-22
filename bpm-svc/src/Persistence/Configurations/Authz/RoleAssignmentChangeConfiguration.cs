using Bpm.Domain.Entities.Authz;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Authz;

public sealed class RoleAssignmentChangeConfiguration : IEntityTypeConfiguration<RoleAssignmentChange>
{
    public void Configure(EntityTypeBuilder<RoleAssignmentChange> b)
    {
        b.ToTable("RoleAssignmentChanges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasConversion<int>().IsRequired();
        b.Property(x => x.Scope).HasConversion<int>().IsRequired();
        b.Property(x => x.RoleCodeSnapshot).HasMaxLength(50).IsRequired();
        b.Property(x => x.ScopeRef).HasMaxLength(200);
        b.HasIndex(x => new { x.TargetUserId, x.CreatedAt });
        b.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
    }
}
