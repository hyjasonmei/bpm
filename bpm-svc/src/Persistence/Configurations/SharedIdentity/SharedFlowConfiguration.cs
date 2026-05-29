using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedFlowConfiguration : IEntityTypeConfiguration<SharedFlow>
{
    public void Configure(EntityTypeBuilder<SharedFlow> b)
    {
        b.ToTable("Admin_Flows", t => t.ExcludeFromMigrations());
        b.HasKey(f => f.Id);
        b.Property(f => f.FlowCode).IsRequired().HasMaxLength(64);
        b.Property(f => f.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(f => f.State).HasConversion<int>();
        // Admin_Flows has SpecJson / Notes / CreatedByUserId / RowVersion
        // columns; SharedFlow intentionally doesn't model them — EF
        // selects only the listed columns, so they stay admin-side.
    }
}
