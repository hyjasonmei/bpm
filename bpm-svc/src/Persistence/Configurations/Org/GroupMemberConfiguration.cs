using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Org;

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> b)
    {
        b.ToTable("GroupMembers");
        b.HasKey(x => new { x.GroupId, x.PrincipalId });
        b.HasIndex(x => x.PrincipalId);

        b.HasOne(x => x.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Principal)
            .WithMany()
            .HasForeignKey(x => x.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
