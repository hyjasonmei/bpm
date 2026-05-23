using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedUserDeptConfiguration : IEntityTypeConfiguration<SharedUserDept>
{
    public void Configure(EntityTypeBuilder<SharedUserDept> b)
    {
        b.ToTable("Admin_UserDepts", t => t.ExcludeFromMigrations());
        b.HasKey(x => new { x.UserId, x.DeptId });
    }
}
