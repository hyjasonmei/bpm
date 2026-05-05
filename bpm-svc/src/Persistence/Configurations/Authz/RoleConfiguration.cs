using Bpm.Domain.Entities.Authz;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Authz;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Scope).IsRequired();
        b.Property(x => x.FlowCode).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();

        // Coherence: Scope=Flow requires FlowCode; Scope=System requires FlowCode=null.
        // SQLite supports CHECK constraints.
        b.ToTable(t => t.HasCheckConstraint(
            "CK_Roles_ScopeFlowCode",
            "(\"Scope\" = 1 AND \"FlowCode\" IS NULL) OR (\"Scope\" = 2 AND \"FlowCode\" IS NOT NULL)"));
    }
}
