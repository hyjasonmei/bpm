using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class FlowDeploymentConfiguration : IEntityTypeConfiguration<FlowDeployment>
{
    public void Configure(EntityTypeBuilder<FlowDeployment> builder)
    {
        builder.ToTable("FlowDeployments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.FlowId, x.EnvironmentId }).IsUnique();
        builder.HasIndex(x => x.FlowId);
    }
}
