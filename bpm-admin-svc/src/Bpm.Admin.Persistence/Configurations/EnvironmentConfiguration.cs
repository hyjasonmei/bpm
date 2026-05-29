using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class EnvironmentConfiguration : IEntityTypeConfiguration<Bpm.Admin.Domain.Flows.Environment>
{
    public void Configure(EntityTypeBuilder<Bpm.Admin.Domain.Flows.Environment> builder)
    {
        builder.ToTable("Environments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(40);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(80);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder);
    }
}
