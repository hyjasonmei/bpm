using System.Text;
using Bpm.Application.Files;
using Bpm.Persistence;
using Bpm.Persistence.Files;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Files;

/// <summary>
/// Round-trip + safety checks for the core file-storage primitive. Bytes land
/// on disk under a temp folder created per test; the SQLite db is in-memory
/// so the test is hermetic.
/// </summary>
public sealed class FileStorageServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly string _tempRoot;
    private readonly FileStorageOptions _fileOptions;

    public FileStorageServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(new StubClock(), new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using (var seed = new AppDbContext(_options))
            seed.Database.EnsureCreated();

        _tempRoot = Path.Combine(Path.GetTempPath(), "bpm-file-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _fileOptions = new FileStorageOptions { RootPath = _tempRoot, MaxBytes = 1024 };
    }

    public void Dispose()
    {
        _conn.Dispose();
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Upload_then_OpenRead_returns_same_bytes_and_metadata()
    {
        var bytes = Encoding.UTF8.GetBytes("hello chef, signed: lead");
        await using var db = new AppDbContext(_options);
        var svc = new FileStorageService(db, new StubClock(), _fileOptions);

        var meta = await svc.UploadAsync(
            new MemoryStream(bytes),
            fileName: "certificate.pdf",
            contentType: "application/pdf",
            uploadedBy: "user:wilson",
            ct: default);

        Assert.Equal("certificate.pdf", meta.FileName);
        Assert.Equal("application/pdf", meta.ContentType);
        Assert.Equal(bytes.Length, meta.SizeBytes);
        Assert.Equal(64, meta.Sha256.Length);
        Assert.Equal("user:wilson", meta.UploadedBy);

        var opened = await svc.OpenReadAsync(meta.Id, default);
        Assert.NotNull(opened);
        await using var stream = opened!.Value.Content;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Upload_over_cap_throws_and_leaves_no_disk_or_db_trace()
    {
        await using var db = new AppDbContext(_options);
        var svc = new FileStorageService(db, new StubClock(), _fileOptions);
        var oversized = new byte[_fileOptions.MaxBytes + 1];

        await Assert.ThrowsAsync<FileTooLargeException>(() =>
            svc.UploadAsync(new MemoryStream(oversized), "huge.bin", "application/octet-stream", "tester", default));

        // No DB row.
        Assert.Equal(0, await db.FileBlobs.CountAsync());
        // No leftover bytes on disk.
        Assert.Empty(Directory.EnumerateFiles(_tempRoot));
    }

    [Fact]
    public async Task Upload_strips_path_components_from_client_supplied_filename()
    {
        await using var db = new AppDbContext(_options);
        var svc = new FileStorageService(db, new StubClock(), _fileOptions);

        var meta = await svc.UploadAsync(
            new MemoryStream(new byte[] { 1, 2, 3 }),
            fileName: "../../../etc/passwd",
            contentType: "application/octet-stream",
            uploadedBy: "tester",
            ct: default);

        // Path traversal can't escape: only the leaf survives.
        Assert.Equal("passwd", meta.FileName);
    }

    [Fact]
    public async Task GetMetadata_returns_null_for_unknown_id()
    {
        await using var db = new AppDbContext(_options);
        var svc = new FileStorageService(db, new StubClock(), _fileOptions);
        Assert.Null(await svc.GetMetadataAsync(Guid.NewGuid(), default));
    }
}
