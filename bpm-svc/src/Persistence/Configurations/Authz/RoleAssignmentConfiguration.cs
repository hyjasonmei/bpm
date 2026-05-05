using Bpm.Domain.Entities.Authz;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Authz;

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> b)
    {
        b.ToTable("RoleAssignments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Scope).IsRequired();
        b.Property(x => x.ScopeRef).HasMaxLength(128);

        b.HasIndex(x => x.PrincipalId);
        b.HasIndex(x => new { x.RoleId, x.PrincipalId, x.Scope, x.ScopeRef });

        b.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Principal)
            .WithMany()
            .HasForeignKey(x => x.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
