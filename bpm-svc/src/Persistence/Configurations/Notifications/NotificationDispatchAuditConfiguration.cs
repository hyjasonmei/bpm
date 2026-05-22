using Bpm.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Notifications;

public sealed class NotificationDispatchAuditConfiguration : IEntityTypeConfiguration<NotificationDispatchAudit>
{
    public void Configure(EntityTypeBuilder<NotificationDispatchAudit> b)
    {
        b.ToTable("NotificationDispatchAudits");
        b.HasKey(x => x.Id);

        b.Property(x => x.SpecCode).HasMaxLength(80).IsRequired();
        b.Property(x => x.Trigger).HasMaxLength(80).IsRequired();
        b.Property(x => x.NotificationId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Channel).HasMaxLength(40);
        b.Property(x => x.Recipient).HasMaxLength(500);
        b.Property(x => x.Subject).HasMaxLength(500);
        b.Property(x => x.Body);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.Error).HasMaxLength(2000);
        b.Property(x => x.DispatchedAt).IsRequired();

        // Process Admin Console "what went out for this instance" drill-down.
        b.HasIndex(x => new { x.InstanceId, x.DispatchedAt })
            .IsDescending(false, true);

        // Cross-spec audit query "all notifications for LEAVE this week".
        b.HasIndex(x => new { x.SpecCode, x.DispatchedAt })
            .IsDescending(false, true);

        // Triage failed dispatches.
        b.HasIndex(x => new { x.Status, x.DispatchedAt })
            .IsDescending(false, true);
    }
}
