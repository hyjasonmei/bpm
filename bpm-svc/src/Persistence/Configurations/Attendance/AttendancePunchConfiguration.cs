using Bpm.Domain.Entities.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Attendance;

public sealed class AttendancePunchConfiguration : IEntityTypeConfiguration<AttendancePunch>
{
    public void Configure(EntityTypeBuilder<AttendancePunch> b)
    {
        b.ToTable("AttendancePunches");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.PunchType).HasConversion<int>().IsRequired();
        b.Property(x => x.Source).HasConversion<int>().IsRequired();
        b.Property(x => x.PunchAt).IsRequired();
        b.Property(x => x.LocalDate).IsRequired();

        b.HasIndex(x => new { x.UserId, x.LocalDate });
        b.HasIndex(x => new { x.UserId, x.PunchAt });
    }
}
