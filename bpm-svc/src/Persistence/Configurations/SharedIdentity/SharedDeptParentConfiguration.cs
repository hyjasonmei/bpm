using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedDeptParentConfiguration : IEntityTypeConfiguration<SharedDeptParent>
{
    public void Configure(EntityTypeBuilder<SharedDeptParent> b)
    {
        b.ToTable("Admin_DeptParents", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.DeptId);
    }
}
