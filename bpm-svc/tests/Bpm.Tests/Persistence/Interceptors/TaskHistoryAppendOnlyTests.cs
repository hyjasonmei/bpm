using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Interceptors;

public sealed class TaskHistoryAppendOnlyTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AuditSaveChangesInterceptor _interceptor;

    public TaskHistoryAppendOnlyTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        _interceptor = new AuditSaveChangesInterceptor(new StubClock(), new StubCurrentUser());

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(_interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }

    private TaskHistory SeedHistory()
    {
        using var db = new AppDbContext(_options);
        var instance = new ProcessInstance
        {
            Id = Guid.NewGuid(),
            TenantCode = "default",
            SpecCode = "TEST",
            SpecVersion = 1,
            SpecSnapshotJson = "{}",
            InitiatorUserId = Guid.NewGuid(),
            Status = InstanceStatus.Running,
            CurrentFormDataJson = "{}",
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
        };
        db.ProcessInstances.Add(instance);

        var row = new TaskHistory
        {
            Id = Guid.NewGuid(),
            TenantCode = "default",
            ProcessInstanceId = instance.Id,
            TaskId = null,
            EventType = HistoryEventType.InstanceStarted,
            ActorUserId = Guid.NewGuid(),
            PayloadJson = "{\"note\":\"initial\"}",
        };
        db.TaskHistory.Add(row);
        db.SaveChanges();
        return row;
    }

    [Fact]
    public void Modifying_TaskHistory_throws_InvalidOperationException()
    {
        var seeded = SeedHistory();

        using var db = new AppDbContext(_options);
        var loaded = db.TaskHistory.Single(x => x.Id == seeded.Id);
        loaded.PayloadJson = "{\"note\":\"tampered\"}";

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("TaskHistory is append-only", ex.Message);
        Assert.Contains("Modified", ex.Message);
    }

    [Fact]
    public void Removing_TaskHistory_throws_InvalidOperationException()
    {
        var seeded = SeedHistory();

        using var db = new AppDbContext(_options);
        var loaded = db.TaskHistory.Single(x => x.Id == seeded.Id);
        db.TaskHistory.Remove(loaded);

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("TaskHistory is append-only", ex.Message);
        Assert.Contains("Deleted", ex.Message);
    }

    [Fact]
    public void Inserting_TaskHistory_succeeds()
    {
        // Sanity: append is allowed.
        var seeded = SeedHistory();
        using var db = new AppDbContext(_options);
        Assert.NotNull(db.TaskHistory.Single(x => x.Id == seeded.Id));
    }
}
