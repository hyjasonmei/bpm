using Bpm.Domain.Parallel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Parallel;

public sealed class ParallelApprovalGroupConfiguration : IEntityTypeConfiguration<ParallelApprovalGroup>
{
    public void Configure(EntityTypeBuilder<ParallelApprovalGroup> b)
    {
        b.ToTable("ParallelApprovalGroups");
        b.HasKey(x => x.Id);
        b.Property(x => x.FlowCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.GatewayNodeId).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.FlowCode, x.CaseId, x.GatewayNodeId });
        b.HasMany(x => x.Slots)
            .WithOne()
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
