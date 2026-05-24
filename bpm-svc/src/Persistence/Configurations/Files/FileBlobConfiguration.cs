using Bpm.Domain.Entities.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.Files;

public sealed class FileBlobConfiguration : IEntityTypeConfiguration<FileBlob>
{
    public void Configure(EntityTypeBuilder<FileBlob> b)
    {
        b.ToTable("FileBlobs");
        b.HasKey(x => x.Id);

        b.Property(x => x.FileName).HasMaxLength(500).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.UploadedBy).HasMaxLength(200).IsRequired();

        // Listing recent uploads (admin tooling) and dedup-by-hash lookups.
        b.HasIndex(x => x.UploadedAt).IsDescending(true);
        b.HasIndex(x => x.Sha256);
    }
}
