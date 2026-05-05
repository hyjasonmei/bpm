using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Org;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("Departments");
        b.Property(x => x.Code).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.ParentId);
        b.HasIndex(x => x.HeadUserId);

        b.HasOne(x => x.Parent)
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Head)
            .WithMany()
            .HasForeignKey(x => x.HeadUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
