using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Process;

public sealed class ProcessInstanceConfiguration : IEntityTypeConfiguration<ProcessInstance>
{
    public void Configure(EntityTypeBuilder<ProcessInstance> b)
    {
        b.ToTable("ProcessInstances");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.SpecCode).HasMaxLength(128).IsRequired();
        b.Property(x => x.SpecVersion).IsRequired();
        b.Property(x => x.SpecSnapshotJson).IsRequired();
        b.Property(x => x.InitiatorUserId).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.CurrentFormDataJson).IsRequired();
        b.Property(x => x.StartedAt).IsRequired();
        b.Property(x => x.LastActivityAt).IsRequired();
        b.Property(x => x.CancelReason).HasMaxLength(500);
        b.Property(x => x.LastError).HasMaxLength(2000);

        b.HasIndex(x => new { x.TenantCode, x.Status, x.LastActivityAt });
        b.HasIndex(x => new { x.InitiatorUserId, x.StartedAt });
        b.HasIndex(x => new { x.SpecCode, x.SpecVersion });
    }
}
