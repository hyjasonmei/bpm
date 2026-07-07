using Bpm.Domain.Entities.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Attendance;

public sealed class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> b)
    {
        b.ToTable("AttendanceCorrections");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Date).IsRequired();
        b.Property(x => x.PunchType).HasConversion<int>().IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.RequestedPunchAt).IsRequired();
        b.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        b.Property(x => x.DecisionNote).HasMaxLength(500);

        b.HasIndex(x => new { x.UserId, x.Status });
        b.HasIndex(x => x.Status);
    }
}
