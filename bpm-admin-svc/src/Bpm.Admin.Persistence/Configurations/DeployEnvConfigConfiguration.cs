using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public sealed class DeployEnvConfigConfiguration : IEntityTypeConfiguration<DeployEnvConfig>
{
    public void Configure(EntityTypeBuilder<DeployEnvConfig> b)
    {
        b.ToTable("DeployEnvConfigs");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.EnvName).IsUnique();
        b.Property(x => x.EnvName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ResourceGroup).HasMaxLength(200).IsRequired();
        b.Property(x => x.BpmSvcApp).HasMaxLength(200).IsRequired();
        b.Property(x => x.AdminSvcApp).HasMaxLength(200).IsRequired();
        b.Property(x => x.BpmUiSwa).HasMaxLength(200).IsRequired();
        b.Property(x => x.AdminUiSwa).HasMaxLength(200).IsRequired();
    }
}
