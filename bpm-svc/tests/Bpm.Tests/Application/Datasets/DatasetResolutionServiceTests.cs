using System.Text.Json;
using Bpm.Application.Datasets;
using Bpm.Persistence;
using Bpm.Persistence.Datasets;
using Bpm.Persistence.SharedIdentity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Application.Datasets;

public class DatasetResolutionServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _opts;

    public DatasetResolutionServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:"); _conn.Open();
        _opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = new AppDbContext(_opts);
        db.Database.EnsureCreated();
        // ExcludeFromMigrations tables aren't created by EnsureCreated — make them:
        db.Database.ExecuteSqlRaw(@"CREATE TABLE Admin_Datasets(Id TEXT PRIMARY KEY, Key TEXT, Name TEXT, Description TEXT, ColumnsJson TEXT, IsActive INTEGER, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);");
        db.Database.ExecuteSqlRaw(@"CREATE TABLE Admin_DatasetRows(Id TEXT PRIMARY KEY, DatasetId TEXT, CellsJson TEXT, IsActive INTEGER, SortOrder INTEGER, CreatedAt TEXT, UpdatedAt TEXT, DeletedAt TEXT);");
        Seed(db);
    }

    private static void Seed(AppDbContext db)
    {
        var dsId = Guid.NewGuid();
        db.SharedDatasets.Add(new SharedDataset { Id = dsId, Key = "tw-regions", Name = "R", ColumnsJson = "[]", IsActive = true });
        void Row(string city, string district, bool active = true, int order = 0) =>
            db.SharedDatasetRows.Add(new SharedDatasetRow { Id = Guid.NewGuid(), DatasetId = dsId,
                CellsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["city"] = city, ["district"] = district }),
                IsActive = active, SortOrder = order });
        Row("台北市", "大安區", true, 1); Row("台北市", "信義區", true, 2);
        Row("新北市", "板橋區", true, 3); Row("新北市", "板橋區", true, 4);   // dup district under same city -> distinct test
        Row("台中市", "西屯區", false, 5);                                   // inactive -> excluded
        db.SaveChanges();
    }

    private DatasetResolutionService Svc() => new(new AppDbContext(_opts));

    [Fact]
    public async Task Filter_by_parent_returns_only_matching_rows()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions", "district", null, "city", "台北市", false, null, null), default);
        Assert.Equal(new[] { "大安區", "信義區" }, res.Select(o => o.Value).ToArray());
    }

    [Fact]
    public async Task Distinct_dedupes_repeated_values()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions", "district", null, "city", "新北市", true, null, null), default);
        Assert.Equal(new[] { "板橋區" }, res.Select(o => o.Value).ToArray());
    }

    [Fact]
    public async Task Inactive_rows_excluded()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions", "district", null, "city", "台中市", false, null, null), default);
        Assert.Empty(res);
    }

    [Fact]
    public async Task Missing_filter_value_with_filter_column_returns_empty()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions", "district", null, "city", null, false, null, null), default);
        Assert.Empty(res);
    }

    [Fact]
    public async Task No_filter_column_returns_all_active_distinct_cities()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions", "city", null, null, null, true, null, null), default);
        Assert.Equal(new[] { "台北市", "新北市" }, res.Select(o => o.Value).ToArray());
    }

    [Fact]
    public async Task Label_defaults_to_value_and_group_populates()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions", "district", null, null, null, false, "city", null), default);
        Assert.All(res, o => Assert.Equal(o.Value, o.Label));
        Assert.Contains(res, o => o.Group == "台北市");
    }

    public void Dispose() => _conn.Dispose();
}
