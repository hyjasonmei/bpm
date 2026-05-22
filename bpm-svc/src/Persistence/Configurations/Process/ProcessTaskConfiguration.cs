using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Process;

public sealed class ProcessTaskConfiguration : IEntityTypeConfiguration<ProcessTask>
{
    public void Configure(EntityTypeBuilder<ProcessTask> b)
    {
        b.ToTable("ProcessTasks");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.NodeId).HasMaxLength(128).IsRequired();
        b.Property(x => x.NodeKind).HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.Decision).HasConversion<int?>();
        b.Property(x => x.Comment).HasMaxLength(2000);

        b.HasIndex(x => new { x.ActualAssigneeUserId, x.Status });
        b.HasIndex(x => new { x.ProcessInstanceId, x.NodeId });
        b.HasIndex(x => new { x.Status, x.DueAt });

        b.HasOne(x => x.Instance)
            .WithMany()
            .HasForeignKey(x => x.ProcessInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
