using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedPrincipalRoleConfiguration : IEntityTypeConfiguration<SharedPrincipalRole>
{
    public void Configure(EntityTypeBuilder<SharedPrincipalRole> b)
    {
        b.ToTable("Admin_PrincipalRoles", t => t.ExcludeFromMigrations());
        b.HasKey(x => new { x.PrincipalId, x.RoleId });
    }
}
