using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedDeptHeadConfiguration : IEntityTypeConfiguration<SharedDeptHead>
{
    public void Configure(EntityTypeBuilder<SharedDeptHead> b)
    {
        b.ToTable("Admin_DeptHeads", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.DeptId);
    }
}
