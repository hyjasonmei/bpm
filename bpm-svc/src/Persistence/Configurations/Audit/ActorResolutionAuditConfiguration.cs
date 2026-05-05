using Bpm.Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Audit;

public sealed class ActorResolutionAuditConfiguration : IEntityTypeConfiguration<ActorResolutionAudit>
{
    public void Configure(EntityTypeBuilder<ActorResolutionAudit> b)
    {
        b.ToTable("ActorResolutionAudits");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RequestId).HasMaxLength(64).IsRequired();
        b.Property(x => x.ActorRefJson).IsRequired();
        b.Property(x => x.FlowCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.StepCode).HasMaxLength(64);
        b.Property(x => x.ResultKind).HasMaxLength(16).IsRequired();
        b.Property(x => x.ErrorKind).HasMaxLength(64);

        b.HasIndex(x => x.Timestamp);
        b.HasIndex(x => x.SubmitterUserId);
    }
}
