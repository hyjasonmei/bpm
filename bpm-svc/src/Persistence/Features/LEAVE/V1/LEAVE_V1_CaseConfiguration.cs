using Bpm.Domain.Features.LEAVE.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Features.LEAVE.V1;

public sealed class LEAVE_V1_CaseConfiguration : IEntityTypeConfiguration<LEAVE_V1_Case>
{
    public void Configure(EntityTypeBuilder<LEAVE_V1_Case> b)
    {
        b.ToTable("LEAVE_V1_leave_case");
        b.HasKey(c => c.Id);

        b.Property(c => c.LeaveType).IsRequired().HasMaxLength(32);
        b.Property(c => c.Reason).IsRequired().HasMaxLength(2000);
        b.Property(c => c.Days).HasPrecision(5, 2);
        b.Property(c => c.ArchiveNote).HasMaxLength(2000);
        b.Property(c => c.ManagerComment).HasMaxLength(2000);
        b.Property(c => c.VpComment).HasMaxLength(2000);
        b.Property(c => c.Status).HasConversion<int>();

        b.Property(c => c.CurrentAssigneeRoleCode).HasMaxLength(60);
        b.HasIndex(c => new { c.CurrentAssigneeRoleCode, c.LastActivityAt });

        b.HasIndex(c => c.SubmitterUserId);
        b.HasIndex(c => c.CurrentAssigneeUserId);
        b.HasIndex(c => new { c.Status, c.LastActivityAt });
    }
}
