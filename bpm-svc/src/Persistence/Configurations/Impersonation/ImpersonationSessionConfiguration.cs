using Bpm.Domain.Entities.Impersonation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Impersonation;

public sealed class ImpersonationSessionConfiguration : IEntityTypeConfiguration<ImpersonationSession>
{
    public void Configure(EntityTypeBuilder<ImpersonationSession> b)
    {
        b.ToTable("ImpersonationSessions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.EndReason).HasConversion<int>();

        b.HasIndex(x => new { x.ImpersonatorUserId, x.StartedAt });
        b.HasIndex(x => x.EndedAt);
    }
}
