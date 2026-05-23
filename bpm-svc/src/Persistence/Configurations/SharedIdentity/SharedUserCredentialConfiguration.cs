using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedUserCredentialConfiguration : IEntityTypeConfiguration<SharedUserCredential>
{
    public void Configure(EntityTypeBuilder<SharedUserCredential> b)
    {
        b.ToTable("Admin_UserCredentials", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.UserId);
        b.Property(x => x.PasswordHash).IsRequired().HasMaxLength(500);
    }
}
