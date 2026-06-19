using Bpm.Domain.Features.WFH.V4;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.WFH.V4;

public sealed class WFH_V4_CaseConfiguration : IEntityTypeConfiguration<WFH_V4_Case>
{
    public void Configure(EntityTypeBuilder<WFH_V4_Case> b)
    {
        b.ToTable("WFH_V4_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.Reason).IsRequired().HasMaxLength(2000);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.SeniorComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
