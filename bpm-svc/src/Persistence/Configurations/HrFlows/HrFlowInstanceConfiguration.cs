using Bpm.Domain.Entities.HrFlows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.HrFlows;

public sealed class HrFlowInstanceConfiguration : IEntityTypeConfiguration<HrFlowInstance>
{
    public void Configure(EntityTypeBuilder<HrFlowInstance> b)
    {
        b.ToTable("HrFlowInstances");
        b.HasKey(x => x.Id);

        b.Property(x => x.SpecCode).HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.CurrentStep).HasConversion<int>().IsRequired();
        b.Property(x => x.FormDataJson).IsRequired();
        b.Property(x => x.StartedAt).IsRequired();
        b.Property(x => x.LastActivityAt).IsRequired();

        b.HasIndex(x => new { x.InitiatorUserId, x.LastActivityAt });
        b.HasIndex(x => new { x.ResolvedManagerUserId, x.Status });
        b.HasIndex(x => x.Status);

        b.HasMany(x => x.Actions)
            .WithOne(a => a.Instance)
            .HasForeignKey(a => a.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
