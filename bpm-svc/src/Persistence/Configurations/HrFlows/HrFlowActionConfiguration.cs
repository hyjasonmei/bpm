using Bpm.Domain.Entities.HrFlows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.HrFlows;

public sealed class HrFlowActionConfiguration : IEntityTypeConfiguration<HrFlowAction>
{
    public void Configure(EntityTypeBuilder<HrFlowAction> b)
    {
        b.ToTable("HrFlowActions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Action).HasConversion<int>().IsRequired();
        b.Property(x => x.FromStep).HasConversion<int>().IsRequired();
        b.Property(x => x.ToStep).HasConversion<int>().IsRequired();
        b.Property(x => x.Comment).HasMaxLength(2000);

        b.HasIndex(x => new { x.InstanceId, x.CreatedAt });
    }
}
