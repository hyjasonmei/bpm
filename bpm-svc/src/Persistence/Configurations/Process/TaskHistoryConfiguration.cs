using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Process;

public sealed class TaskHistoryConfiguration : IEntityTypeConfiguration<TaskHistory>
{
    public void Configure(EntityTypeBuilder<TaskHistory> b)
    {
        b.ToTable("TaskHistory");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.EventType).HasConversion<int>().IsRequired();
        b.Property(x => x.PayloadJson).IsRequired();

        b.HasIndex(x => new { x.ProcessInstanceId, x.CreatedAt });
        b.HasIndex(x => x.TaskId);
        b.HasIndex(x => new { x.EventType, x.CreatedAt });

        b.HasOne(x => x.Instance)
            .WithMany()
            .HasForeignKey(x => x.ProcessInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
