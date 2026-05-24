namespace Bpm.Application.Files.Dtos;

/// <summary>
/// What chef-cooked feature code (and the FilePicker UI) sees back after an
/// upload. The id is what the feature stores in its form-data JSON; everything
/// else is for rendering the chip / preview.
/// </summary>
public sealed record FileMetadataDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string UploadedBy,
    DateTime UploadedAt);
