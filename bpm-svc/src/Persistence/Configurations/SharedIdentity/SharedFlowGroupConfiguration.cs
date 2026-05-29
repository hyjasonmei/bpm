using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedFlowGroupConfiguration : IEntityTypeConfiguration<SharedFlowGroup>
{
    public void Configure(EntityTypeBuilder<SharedFlowGroup> b)
    {
        b.ToTable("Admin_FlowGroups", t => t.ExcludeFromMigrations());
        b.HasKey(g => g.Id);
        b.Property(g => g.Code).IsRequired().HasMaxLength(40);
        b.Property(g => g.DisplayNameJson).IsRequired();
        b.Property(g => g.Icon).HasMaxLength(60);
    }
}
