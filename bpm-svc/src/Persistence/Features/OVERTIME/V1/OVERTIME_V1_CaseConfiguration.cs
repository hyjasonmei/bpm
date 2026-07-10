using Bpm.Domain.Features.OVERTIME.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.OVERTIME.V1;

/// <summary>EF mapping for the OVERTIME V1 case table.</summary>
public sealed class OVERTIME_V1_CaseConfiguration : IEntityTypeConfiguration<OVERTIME_V1_Case>
{
    public void Configure(EntityTypeBuilder<OVERTIME_V1_Case> b)
    {
        b.ToTable("OVERTIME_V1_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.OvertimeReason).IsRequired().HasMaxLength(2000);
        b.Property(c => c.StartTime).HasMaxLength(20);
        b.Property(c => c.EndTime).HasMaxLength(20);
        b.Property(c => c.EstimatedHours).HasPrecision(9, 2);
        b.Property(c => c.MonthlyHours).HasPrecision(9, 2);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.HrComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.Property(c => c.CurrentAssigneeRoleCode).HasMaxLength(60);
        b.HasIndex(c => new { c.CurrentAssigneeRoleCode, c.LastActivityAt });

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
        // Backs the monthly-cumulative-hours gateway query.
        b.HasIndex(c => new { c.SubmitterUserId, c.Status, c.OvertimeDate });
    }
}
