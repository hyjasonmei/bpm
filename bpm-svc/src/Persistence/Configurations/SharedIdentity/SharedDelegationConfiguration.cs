using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedDelegationConfiguration : IEntityTypeConfiguration<SharedDelegation>
{
    public void Configure(EntityTypeBuilder<SharedDelegation> b)
    {
        b.ToTable("Admin_Delegations", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
    }
}
