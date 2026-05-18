using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class FlowConfiguration : IEntityTypeConfiguration<Flow>
{
    public void Configure(EntityTypeBuilder<Flow> builder)
    {
        builder.ToTable("Flows");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FlowCode).IsRequired().HasMaxLength(40);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SpecJson).IsRequired();
        builder.Property(x => x.State).HasConversion<int>();

        builder.HasIndex(x => x.LineageId);
        builder.HasIndex(x => new { x.LineageId, x.Version }).IsUnique();
        builder.HasIndex(x => x.State);
        builder.HasIndex(x => x.UpdatedAt);
    }
}
