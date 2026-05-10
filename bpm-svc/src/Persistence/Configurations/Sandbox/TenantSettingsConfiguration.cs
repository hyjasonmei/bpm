using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Sandbox;

public sealed class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> b)
    {
        b.ToTable("TenantSettings");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TenantCode).IsUnique();
        b.Property(x => x.TenantCode).HasMaxLength(50).IsRequired();
    }
}
