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

        b.Property(c => c.CaseTitle).IsRequired().HasMaxLength(300);
        b.Property(c => c.ReviewCategory).IsRequired().HasMaxLength(64);
        b.Property(c => c.ApplyAmount).HasColumnType("decimal(18,2)");
        b.Property(c => c.BenefitDescription).IsRequired().HasMaxLength(2000);
        b.Property(c => c.Remarks).HasMaxLength(2000);
        b.Property(c => c.RevisionNote).HasMaxLength(2000);
        b.Property(c => c.CeoComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
