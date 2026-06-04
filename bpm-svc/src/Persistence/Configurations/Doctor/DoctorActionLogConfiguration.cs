using Bpm.Domain.Entities.Doctor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Doctor;

public sealed class DoctorActionLogConfiguration : IEntityTypeConfiguration<DoctorActionLog>
{
    public void Configure(EntityTypeBuilder<DoctorActionLog> b)
    {
        b.ToTable("DoctorActionLogs");
        b.HasKey(x => x.Id);

        b.Property(x => x.Action).HasMaxLength(40).IsRequired();
        b.Property(x => x.FlowCode).HasMaxLength(64);
        b.Property(x => x.Reason).HasMaxLength(500);

        b.HasIndex(x => x.CreatedAt).IsDescending();
    }
}
