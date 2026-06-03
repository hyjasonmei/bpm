using Bpm.Domain.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Notifications;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.ToTable("UserNotifications");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Title).HasMaxLength(500).IsRequired();
        b.Property(x => x.Body);
        b.Property(x => x.Link).HasMaxLength(500);
        b.Property(x => x.SourceId).HasMaxLength(200);
        b.Property(x => x.FlowCode).HasMaxLength(80);
        b.Property(x => x.IsRead).IsRequired();
        b.Property(x => x.CreatedAt).IsRequired();

        // Bell query: "my notifications, unread first hint, newest first".
        b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt })
            .IsDescending(false, false, true);
    }
}
