using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public void Configure(EntityTypeBuilder<Principal> builder)
    {
        builder.ToTable("Principals");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Email)
            .HasMaxLength(320);

        builder.Property(p => p.Type)
            .HasConversion<int>();

        builder.HasIndex(p => p.Type);

        builder.HasIndex(p => p.Email)
            .HasFilter("\"Email\" IS NOT NULL");
    }
}
