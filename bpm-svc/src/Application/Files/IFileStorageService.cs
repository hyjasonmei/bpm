using Bpm.Application.Files.Dtos;

namespace Bpm.Application.Files;

/// <summary>
/// Core file-storage primitive consumed by feature code via the FilePicker UI
/// component (which POSTs to /api/files) and by handlers that need to read a
/// previously-uploaded blob back.
/// </summary>
/// <remarks>
/// Bytes-on-disk in POC; the interface keeps options open for object-storage
/// in prod. Feature code never reads/writes disk directly — it only handles
/// the <see cref="FileMetadataDto.Id"/> Guid passed through form data.
/// </remarks>
public interface IFileStorageService
{
    Task<FileMetadataDto> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string uploadedBy,
        CancellationToken ct);

    Task<FileMetadataDto?> GetMetadataAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Opens a read stream over the stored bytes. Caller owns disposal.
    /// Returns null when the id is unknown (callers should 404).
    /// </summary>
    Task<(FileMetadataDto Meta, Stream Content)?> OpenReadAsync(Guid id, CancellationToken ct);
}

/// <summary>Thrown when an upload exceeds the configured per-file size cap.</summary>
public sealed class FileTooLargeException(long sizeBytes, long capBytes)
    : Exception($"Uploaded file is {sizeBytes} bytes; cap is {capBytes} bytes")
{
    public long SizeBytes { get; } = sizeBytes;
    public long CapBytes { get; } = capBytes;
}
