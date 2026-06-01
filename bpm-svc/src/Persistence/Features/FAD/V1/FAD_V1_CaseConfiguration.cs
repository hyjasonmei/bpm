using Bpm.Domain.Features.FAD.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.FAD.V1;

public sealed class FAD_V1_CaseConfiguration : IEntityTypeConfiguration<FAD_V1_Case>
{
    public void Configure(EntityTypeBuilder<FAD_V1_Case> b)
    {
        b.ToTable("FAD_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.DisposalReason).IsRequired().HasMaxLength(40);
        b.Property(c => c.AssetId).IsRequired().HasMaxLength(100);
        b.Property(c => c.AssetName).IsRequired().HasMaxLength(200);
        b.Property(c => c.Description).HasMaxLength(2000);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.HandlingResult).HasMaxLength(40);
        b.Property(c => c.ConfirmRemark).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
