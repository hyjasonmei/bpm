using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class UserManagerConfiguration : IEntityTypeConfiguration<UserManager>
{
    public void Configure(EntityTypeBuilder<UserManager> b)
    {
        b.ToTable("UserManagers");
        b.HasKey(x => x.UserId);
        b.HasIndex(x => x.ManagerUserId);
    }
}
