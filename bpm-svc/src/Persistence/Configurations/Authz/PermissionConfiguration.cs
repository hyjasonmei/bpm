using Bpm.Domain.Entities.Authz;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Authz;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Action).HasMaxLength(64).IsRequired();
        b.Property(x => x.Resource).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.Action, x.Resource }).IsUnique();
    }
}
