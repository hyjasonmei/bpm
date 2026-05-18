using Bpm.Admin.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class PrincipalRoleConfiguration : IEntityTypeConfiguration<PrincipalRole>
{
    public void Configure(EntityTypeBuilder<PrincipalRole> builder)
    {
        builder.ToTable("PrincipalRoles");
        builder.HasKey(x => new { x.PrincipalId, x.RoleId });
        builder.HasIndex(x => x.PrincipalId);
        builder.HasIndex(x => x.RoleId);
    }
}
