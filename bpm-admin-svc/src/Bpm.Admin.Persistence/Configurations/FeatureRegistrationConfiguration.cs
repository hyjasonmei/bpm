using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class FeatureRegistrationConfiguration : IEntityTypeConfiguration<FeatureRegistration>
{
    public void Configure(EntityTypeBuilder<FeatureRegistration> builder)
    {
        builder.ToTable("FeatureRegistrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FlowCode).IsRequired().HasMaxLength(40);
        builder.Property(x => x.TableNamesJson).IsRequired();
        builder.HasIndex(x => new { x.FlowCode, x.Version });
        builder.HasIndex(x => x.FlowId);
    }
}
