using Bpm.Admin.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(e => e.EventId);

        builder.Property(e => e.ActionType).IsRequired().HasMaxLength(80);
        builder.Property(e => e.TargetType).IsRequired().HasMaxLength(80);
        builder.Property(e => e.TargetId).HasMaxLength(100);
        builder.Property(e => e.SourceSystem).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(500);

        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.ActionType);
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => e.SourceSystem);
    }
}
