using Bpm.Application.Common.Abstractions;
using Bpm.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Tests.Common;

internal sealed class TestDb : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _conn;

    public AppDbContext Context { get; }

    private TestDb(SqliteConnection conn, AppDbContext ctx)
    {
        _conn = conn;
        Context = ctx;
    }

    public static async Task<TestDb> CreateAsync()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        await ctx.Database.EnsureCreatedAsync();
        return new TestDb(conn, ctx);
    }

    public IAppDbContext AsAppDbContext() => Context;

    public void Dispose()
    {
        Context.Dispose();
        _conn.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _conn.DisposeAsync();
    }
}
