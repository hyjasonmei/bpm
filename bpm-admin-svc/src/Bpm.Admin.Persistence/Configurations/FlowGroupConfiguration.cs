using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class FlowGroupConfiguration : IEntityTypeConfiguration<FlowGroup>
{
    public void Configure(EntityTypeBuilder<FlowGroup> builder)
    {
        builder.ToTable("FlowGroups");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(40);
        builder.Property(x => x.DisplayNameJson).IsRequired();
        builder.Property(x => x.Icon).HasMaxLength(60);

        // Uniqueness gate per live row (soft-delete filter masks deleted
        // rows so admin can reuse a code after retirement).
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder);
    }
}
