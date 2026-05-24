using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedGroupMemberConfiguration : IEntityTypeConfiguration<SharedGroupMember>
{
    public void Configure(EntityTypeBuilder<SharedGroupMember> b)
    {
        b.ToTable("Admin_GroupMembers", t => t.ExcludeFromMigrations());
        b.HasKey(x => new { x.GroupId, x.MemberPrincipalId });
        b.Property(x => x.MemberType).HasConversion<int>();
    }
}
