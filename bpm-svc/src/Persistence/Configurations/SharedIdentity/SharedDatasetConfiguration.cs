using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedDatasetConfiguration : IEntityTypeConfiguration<SharedDataset>
{
    public void Configure(EntityTypeBuilder<SharedDataset> b)
    {
        b.ToTable("Admin_Datasets", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(60);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.ColumnsJson).IsRequired();
    }
}

public sealed class SharedDatasetRowConfiguration : IEntityTypeConfiguration<SharedDatasetRow>
{
    public void Configure(EntityTypeBuilder<SharedDatasetRow> b)
    {
        b.ToTable("Admin_DatasetRows", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.CellsJson).IsRequired();
    }
}
