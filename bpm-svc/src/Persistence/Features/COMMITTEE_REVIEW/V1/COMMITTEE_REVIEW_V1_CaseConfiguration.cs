using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.COMMITTEE_REVIEW.V1;

public sealed class COMMITTEE_REVIEW_V1_CaseConfiguration : IEntityTypeConfiguration<COMMITTEE_REVIEW_V1_Case>
{
    public void Configure(EntityTypeBuilder<COMMITTEE_REVIEW_V1_Case> b)
    {
        b.ToTable("COMMITTEE_REVIEW_V1_case");
        b.HasKey(c => c.Id);
        b.Property(c => c.Title).IsRequired().HasMaxLength(300);
        b.Property(c => c.Purpose).IsRequired().HasMaxLength(2000);
        b.Property(c => c.Amount).HasColumnType("decimal(18,2)");
        b.Property(c => c.Currency).IsRequired().HasMaxLength(10);
        b.Property(c => c.Status).HasConversion<int>();
        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
