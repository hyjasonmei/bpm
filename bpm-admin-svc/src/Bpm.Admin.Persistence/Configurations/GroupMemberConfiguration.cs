using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("GroupMembers");
        builder.HasKey(x => new { x.GroupId, x.MemberPrincipalId });
        builder.HasIndex(x => x.GroupId);
        builder.HasIndex(x => x.MemberPrincipalId);

        builder.Property(x => x.MemberType).HasConversion<int>();
    }
}
