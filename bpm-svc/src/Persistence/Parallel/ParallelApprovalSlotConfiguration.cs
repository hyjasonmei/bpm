using Bpm.Domain.Parallel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Parallel;

public sealed class ParallelApprovalSlotConfiguration : IEntityTypeConfiguration<ParallelApprovalSlot>
{
    public void Configure(EntityTypeBuilder<ParallelApprovalSlot> b)
    {
        b.ToTable("ParallelApprovalSlots");
        b.HasKey(x => x.Id);
        b.Property(x => x.NodeId).HasMaxLength(128).IsRequired();
        b.Property(x => x.AssigneeRoleCode).HasMaxLength(64);
        b.Property(x => x.Comment).HasMaxLength(2000);
        // Inbox queries: "pending slots for this role" / "for this user".
        b.HasIndex(x => new { x.AssigneeRoleCode, x.Decision });
        b.HasIndex(x => new { x.AssigneeUserId, x.Decision });
    }
}
