using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class DeptHeadConfiguration : IEntityTypeConfiguration<DeptHead>
{
    public void Configure(EntityTypeBuilder<DeptHead> b)
    {
        b.ToTable("DeptHeads");
        b.HasKey(x => x.DeptId);
        b.HasIndex(x => x.HeadUserId);
    }
}
