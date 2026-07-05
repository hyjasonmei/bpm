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
        builder.Property(x => x.IconKey).HasMaxLength(64);
        builder.Property(x => x.DisplayOrder).HasDefaultValue(0);

        builder.HasIndex(x => x.LineageId);
        builder.HasIndex(x => new { x.LineageId, x.Version }).IsUnique();
        // One LIVE row per (code, version): retired/archived/deleted history is
        // exempt so a retire→re-cook can coexist with its predecessor, but two
        // rows both visible to launcher resolution can never share a version.
        // Filter is raw SQL by EF design — double-quoted identifiers and the
        // literal 7 (= FlowState.Retired) parse identically on SQLite and
        // Postgres. Keep in sync with the FlowState enum if it is renumbered.
        builder.HasIndex(x => new { x.FlowCode, x.Version })
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL AND \"DeletedAt\" IS NULL AND \"State\" <> 7");
        builder.HasIndex(x => x.State);
        builder.HasIndex(x => x.UpdatedAt);
        builder.HasIndex(x => x.GroupId);
    }
}
