using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class UserDeptConfiguration : IEntityTypeConfiguration<UserDept>
{
    public void Configure(EntityTypeBuilder<UserDept> builder)
    {
        builder.ToTable("UserDepts");
        builder.HasKey(x => new { x.UserId, x.DeptId });
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.DeptId);
    }
}
