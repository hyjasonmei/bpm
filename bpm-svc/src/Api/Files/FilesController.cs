using Bpm.Api.Auth;
using Bpm.Application.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Files;

/// <summary>
/// Core file-upload endpoint. Feature folders never talk to disk; they POST
/// here via the FilePicker UI primitive, store the returned id in form data,
/// and dereference through GET /api/files/{id} when they need the bytes back.
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController(IFileStorageService storage) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)] // hard outer cap; service enforces the configured per-file cap inside.
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "file_required" });

        var uploader = User.FindFirst("sub")?.Value ?? "system";
        try
        {
            await using var stream = file.OpenReadStream();
            var meta = await storage.UploadAsync(stream, file.FileName, file.ContentType ?? "application/octet-stream", uploader, ct);
            return Ok(meta);
        }
        catch (FileTooLargeException ex)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "file_too_large", cap = ex.CapBytes });
        }
    }

    [HttpGet("{id:guid}/meta")]
    public async Task<IActionResult> GetMeta(Guid id, CancellationToken ct)
    {
        var meta = await storage.GetMetadataAsync(id, ct);
        return meta is null ? NotFound() : Ok(meta);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await storage.OpenReadAsync(id, ct);
        if (result is null) return NotFound();
        var (meta, content) = result.Value;
        return File(content, meta.ContentType, meta.FileName);
    }
}
