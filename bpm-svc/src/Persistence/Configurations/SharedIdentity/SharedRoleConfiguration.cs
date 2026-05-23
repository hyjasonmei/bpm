using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedRoleConfiguration : IEntityTypeConfiguration<SharedRole>
{
    public void Configure(EntityTypeBuilder<SharedRole> b)
    {
        b.ToTable("Admin_Roles", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
    }
}
