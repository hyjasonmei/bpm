using Bpm.Admin.Domain.Datasets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
{
    public void Configure(EntityTypeBuilder<Dataset> b)
    {
        b.ToTable("Datasets");                       // -> Admin_Datasets via ApplyAdminTablePrefix
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(60);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.ColumnsJson).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
    }
}

public class DatasetRowConfiguration : IEntityTypeConfiguration<DatasetRow>
{
    public void Configure(EntityTypeBuilder<DatasetRow> b)
    {
        b.ToTable("DatasetRows");                    // -> Admin_DatasetRows
        b.HasKey(x => x.Id);
        b.Property(x => x.CellsJson).IsRequired();
        b.HasIndex(x => x.DatasetId);
        b.HasIndex(x => new { x.DatasetId, x.SortOrder });
    }
}
