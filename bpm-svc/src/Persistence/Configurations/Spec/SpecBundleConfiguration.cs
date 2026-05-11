using Bpm.Domain.Entities.Spec;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Spec;

public sealed class SpecBundleConfiguration : IEntityTypeConfiguration<SpecBundle>
{
    public void Configure(EntityTypeBuilder<SpecBundle> b)
    {
        b.ToTable("SpecBundles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TenantCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.FlowCode).HasMaxLength(128).IsRequired();
        b.Property(x => x.FlowVersion).IsRequired();
        b.Property(x => x.ManifestChecksum).HasMaxLength(64).IsRequired();
        b.Property(x => x.ParentManifestChecksum).HasMaxLength(64);
        b.Property(x => x.ManifestJson).IsRequired();
        b.Property(x => x.ZipBlob).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.LastReproCheckResultJson);

        // Newest version per (tenant, flow) is the common Flow Library list
        // query — descending FlowVersion lets us pull the latest with a
        // single index-ordered scan. Tenant + flow ascending keep the
        // composite usable for filtered "show me all versions of LEAVE"
        // lookups.
        b.HasIndex(x => new { x.TenantCode, x.FlowCode, x.FlowVersion })
            .IsDescending(false, false, true);

        // ManifestChecksum is the bundle's stable identity. Unique so
        // re-importing byte-identical bytes is a no-op (the REST layer
        // returns the existing row rather than persisting a duplicate).
        b.HasIndex(x => x.ManifestChecksum).IsUnique();

        // Status filter is hot for the Flow Library landing page (filter
        // by Installed / InstalledUnverified / Failed).
        b.HasIndex(x => x.Status);
    }
}
