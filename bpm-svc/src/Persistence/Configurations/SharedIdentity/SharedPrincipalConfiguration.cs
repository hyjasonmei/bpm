using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedPrincipalConfiguration : IEntityTypeConfiguration<SharedPrincipal>
{
    public void Configure(EntityTypeBuilder<SharedPrincipal> b)
    {
        b.ToTable("Admin_Principals", t => t.ExcludeFromMigrations());
        b.HasKey(p => p.Id);
        b.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
        b.Property(p => p.Email).HasMaxLength(320);
        b.Property(p => p.Type).HasConversion<int>();
    }
}
