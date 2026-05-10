using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Sandbox;

public sealed class SandboxRedirectConfiguration : IEntityTypeConfiguration<SandboxRedirect>
{
    public void Configure(EntityTypeBuilder<SandboxRedirect> b)
    {
        b.ToTable("SandboxRedirects");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Channel).HasConversion<int>().IsRequired();
        b.Property(x => x.Action).HasConversion<int>().IsRequired();
        b.Property(x => x.SampleSubject).HasMaxLength(200);
        b.HasIndex(x => new { x.TenantCode, x.DispatchedAt });
        b.HasIndex(x => new { x.TenantCode, x.Channel, x.DispatchedAt });
    }
}
