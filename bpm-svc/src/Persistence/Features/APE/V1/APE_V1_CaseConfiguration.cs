using Bpm.Domain.Features.APE.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.APE.V1;

public sealed class APE_V1_CaseConfiguration : IEntityTypeConfiguration<APE_V1_Case>
{
    public void Configure(EntityTypeBuilder<APE_V1_Case> b)
    {
        b.ToTable("APE_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.ChargeDepartment).IsRequired().HasMaxLength(200);
        b.Property(c => c.RechargeOutside).HasMaxLength(10);
        b.Property(c => c.Description).IsRequired().HasMaxLength(2000);
        b.Property(c => c.Currency).IsRequired().HasMaxLength(10);
        b.Property(c => c.Amount).HasColumnType("decimal(18,2)");
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
