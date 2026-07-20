using Bpm.Domain.Entities.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Transfer;

public sealed class CaseTransferLogConfiguration : IEntityTypeConfiguration<CaseTransferLog>
{
    public void Configure(EntityTypeBuilder<CaseTransferLog> b)
    {
        b.ToTable("CaseTransferLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.FlowCode).HasMaxLength(64).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        b.HasIndex(x => x.CaseId);
        b.HasIndex(x => x.OperatorUserId);
        b.HasIndex(x => x.CreatedAt);
    }
}
