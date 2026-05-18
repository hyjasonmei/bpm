using Bpm.Admin.Domain.Delegations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class DelegationConfiguration : IEntityTypeConfiguration<Delegation>
{
    public void Configure(EntityTypeBuilder<Delegation> builder)
    {
        builder.ToTable("Delegations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasIndex(x => x.DelegatorPrincipalId);
        builder.HasIndex(x => x.DelegateToUserId);
        builder.HasIndex(x => new { x.DelegatorPrincipalId, x.Active, x.StartAt, x.EndAt });
    }
}
