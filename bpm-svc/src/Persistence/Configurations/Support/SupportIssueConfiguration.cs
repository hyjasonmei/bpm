using Bpm.Domain.Entities.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Support;

public sealed class SupportIssueConfiguration : IEntityTypeConfiguration<SupportIssue>
{
    public void Configure(EntityTypeBuilder<SupportIssue> b)
    {
        b.ToTable("SupportIssues");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.Kind).IsRequired().HasMaxLength(20);
        b.Property(x => x.Title).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).IsRequired().HasMaxLength(4000);
        b.Property(x => x.Contact).HasMaxLength(200);
        b.Property(x => x.Page).HasMaxLength(300);
        b.Property(x => x.UserAgent).HasMaxLength(400);
        b.Property(x => x.Status).HasConversion<int>().IsRequired();

        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.SubmittedAt);
    }
}
