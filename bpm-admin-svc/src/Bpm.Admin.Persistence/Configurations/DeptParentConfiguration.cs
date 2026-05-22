using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class DeptParentConfiguration : IEntityTypeConfiguration<DeptParent>
{
    public void Configure(EntityTypeBuilder<DeptParent> builder)
    {
        builder.ToTable("DeptParents");
        builder.HasKey(x => x.DeptId);
        builder.HasIndex(x => x.ParentDeptId);
    }
}
