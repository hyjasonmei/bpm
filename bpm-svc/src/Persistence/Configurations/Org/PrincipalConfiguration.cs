using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Org;

public sealed class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public void Configure(EntityTypeBuilder<Principal> b)
    {
        b.ToTable("Principals");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Type).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();

        b.UseTptMappingStrategy();
    }
}
