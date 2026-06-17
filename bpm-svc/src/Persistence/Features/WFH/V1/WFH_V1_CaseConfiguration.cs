using Bpm.Domain.Features.WFH.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.WFH.V1;

public sealed class WFH_V1_CaseConfiguration : IEntityTypeConfiguration<WFH_V1_Case>
{
    public void Configure(EntityTypeBuilder<WFH_V1_Case> b)
    {
        b.ToTable("WFH_V1_case");
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
