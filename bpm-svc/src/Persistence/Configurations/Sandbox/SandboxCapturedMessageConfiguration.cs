using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Sandbox;

public sealed class SandboxCapturedMessageConfiguration : IEntityTypeConfiguration<SandboxCapturedMessage>
{
    public void Configure(EntityTypeBuilder<SandboxCapturedMessage> b)
    {
        b.ToTable("SandboxCapturedMessages");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Channel).HasConversion<int>().IsRequired();

        b.Property(x => x.IntendedRecipientsJson).IsRequired();
        b.Property(x => x.ReadByUserIdsJson).IsRequired();

        b.Property(x => x.FlowCode).HasMaxLength(64);
        b.Property(x => x.Subject).HasMaxLength(500);
        b.Property(x => x.Url).HasMaxLength(2048);
        b.Property(x => x.EventType).HasMaxLength(200);
        b.Property(x => x.OriginatingNotificationId).HasMaxLength(200);
        b.Property(x => x.OriginatingWebhookSubscriptionId).HasMaxLength(200);

        // Main mailbox query: list per-tenant captured messages newest first.
        b.HasIndex(x => new { x.TenantCode, x.CapturedAt })
            .IsDescending(false, true);

        // Per-tab filter (Mail / Webhook / SMS) inside one tenant.
        b.HasIndex(x => new { x.TenantCode, x.Channel, x.CapturedAt })
            .IsDescending(false, false, true);

        // Per-instance drill-down: "what did this instance send out?"
        b.HasIndex(x => new { x.ProcessInstanceId, x.CapturedAt })
            .IsDescending(false, true);

        // Model-B per-flow mailbox filter: "what did flow X send out?"
        b.HasIndex(x => new { x.TenantCode, x.FlowCode, x.CapturedAt })
            .IsDescending(false, false, true);

        // Webhook tab event-type filter.
        b.HasIndex(x => new { x.EventType, x.CapturedAt })
            .IsDescending(false, true);
    }
}
