using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedUserManagerConfiguration : IEntityTypeConfiguration<SharedUserManager>
{
    public void Configure(EntityTypeBuilder<SharedUserManager> b)
    {
        b.ToTable("Admin_UserManagers", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.UserId);
    }
}
