using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Sandbox;

public sealed class FlowSandboxConfigConfiguration : IEntityTypeConfiguration<FlowSandboxConfig>
{
    public void Configure(EntityTypeBuilder<FlowSandboxConfig> b)
    {
        b.ToTable("FlowSandboxConfigs");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.FlowCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(200);

        // One config row per flow per tenant — upsert keys on this.
        b.HasIndex(x => new { x.TenantCode, x.FlowCode }).IsUnique();
    }
}
