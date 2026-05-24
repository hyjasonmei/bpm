using System.Security.Cryptography;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Files;
using Bpm.Application.Files.Dtos;
using Bpm.Domain.Entities.Files;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Files;

public sealed class FileStorageService(
    AppDbContext db,
    IClock clock,
    FileStorageOptions options) : IFileStorageService
{
    private readonly string _root = FileStoragePathResolver.Resolve(options.RootPath);

    public async Task<FileMetadataDto> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string uploadedBy,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName required", nameof(fileName));

        var id = Guid.NewGuid();
        var diskPath = Path.Combine(_root, id.ToString("N"));

        long size;
        string sha256Hex;

        // Stream to disk while hashing in one pass so we don't buffer the
        // whole upload in memory. Enforce the size cap mid-stream so an
        // attacker can't smuggle past it by lying about Content-Length.
        await using (var dst = File.Create(diskPath))
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                total += read;
                if (total > options.MaxBytes)
                {
                    // Bail out: close + delete the partial write before throwing.
                    dst.Close();
                    TryDelete(diskPath);
                    throw new FileTooLargeException(total, options.MaxBytes);
                }
                sha.TransformBlock(buffer, 0, read, null, 0);
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            size = total;
            sha256Hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        var now = clock.UtcNow;
        var entity = new FileBlob
        {
            Id = id,
            FileName = TrimFileName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = size,
            Sha256 = sha256Hex,
            UploadedBy = string.IsNullOrWhiteSpace(uploadedBy) ? "system" : uploadedBy,
            UploadedAt = now,
        };

        db.FileBlobs.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<FileMetadataDto?> GetMetadataAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.FileBlobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<(FileMetadataDto Meta, Stream Content)?> OpenReadAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.FileBlobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return null;

        var diskPath = Path.Combine(_root, entity.Id.ToString("N"));
        if (!File.Exists(diskPath))
        {
            // Metadata without bytes is a corrupt state — surface it as 404.
            return null;
        }

        var stream = new FileStream(
            diskPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return (ToDto(entity), stream);
    }

    private static FileMetadataDto ToDto(FileBlob e) => new(
        e.Id, e.FileName, e.ContentType, e.SizeBytes, e.Sha256, e.UploadedBy, e.UploadedAt);

    /// <summary>Strip any path component from the client-supplied filename.</summary>
    private static string TrimFileName(string fileName)
    {
        // Defense-in-depth: clients sometimes send `path/to/file.pdf`; only the leaf is interesting.
        var leaf = Path.GetFileName(fileName) ?? fileName;
        return leaf.Length > 500 ? leaf[..500] : leaf;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
