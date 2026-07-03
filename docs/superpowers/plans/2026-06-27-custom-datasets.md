# Custom Datasets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a customer admin maintain named reference **datasets** (columnar tables) that flow-form dropdowns bind to — so option values (and cascading parent→child relationships like 縣市→行政區) change without re-cooking or redeploying a flow.

**Architecture:** admin-svc owns the `Dataset`/`DatasetRow` tables + EF migrations (mirrors the existing FlowGroup domain). bpm-svc reads them through `Shared*` DbSets (`ExcludeFromMigrations`, like `SharedFlowGroup`) and exposes a stateless `POST /api/datasets/resolve` that applies filter/distinct/group/sort **in memory** (DB-portable). bpm-ui gets a shared `<DatasetSelect>` primitive that calls resolve and renders options (incl. cascading + `<optgroup>`). bpm-admin-ui gets a "資料集 / Datasets" editor page built on TanStack Table.

**Tech Stack:** .NET 10 / EF Core (SQLite POC, Postgres-ready), xUnit; React 18 + Vite + Tailwind v4 + shadcn; new dep `@tanstack/react-table` (admin-ui only). JSON columns stored as **TEXT** and (de)serialized in the service layer (repo convention — no EF Owned/`.ToJson()`).

---

## Conventions this plan follows (from the codebase)

- **admin-svc auth:** controllers carry **no `[Authorize]`** (global `FallbackPolicy` = authenticated). Admin-only **writes** use `[Authorize(Policy = "SystemAdmin")]` (a `RequireAssertion` policy — do NOT use `[Authorize(Roles=...)]`, it 403s on the array-valued roles claim).
- **bpm-svc auth:** controllers DO carry explicit `[Authorize]` and inherit `BpmControllerBase`.
- **JSON:** stored as a `string` TEXT column, `System.Text.Json` (de)serialized in the service. No EF Owned types / `.ToJson()`.
- **Table prefix:** every admin-svc table is auto-renamed `Admin_<Name>` by `ApplyAdminTablePrefix`. A `Dataset` entity → table `Admin_Datasets`; `DatasetRow` → `Admin_DatasetRows`. Those literal names are what bpm-svc `Shared*` configs map with `ExcludeFromMigrations`.
- **Migrations:** generated from `bpm-admin-svc/src/Bpm.Admin.Api` with `-p ../Bpm.Admin.Persistence -s .`, **with `BPM_DB_PROVIDER=postgres`** so the prod schema isn't written as SQLite TEXT. bpm-svc produces NO migration for the shared tables.
- **Backend tests:** xUnit + in-memory SQLite + `EnsureCreated()` + hand-rolled `CREATE TABLE Admin_*` for the `Shared*` mappings. There is **no `WebApplicationFactory`** anywhere — controller tests `new` the controller with a fake `ClaimsPrincipal`.
- **Frontend tests:** the two SPAs have **no JS test runner**. The repo-honest verification per frontend task is `npx tsc -p tsconfig.app.json --noEmit` **plus** a manual browser check (chrome-devtools). Plan steps reflect that — do not invent vitest.
- **Soft delete / deactivate:** use the `ISoftDeletable` marker + global query filter for soft-delete; use an explicit `IsActive` flag for the "deactivate an option but keep it queryable for history" semantics (these are different — see Task 1).

---

## File structure

**bpm-admin-svc (owns the data):**
- Create `bpm-admin-svc/src/Bpm.Admin.Domain/Datasets/Dataset.cs` — `Dataset` + `DatasetRow` entities.
- Create `bpm-admin-svc/src/Bpm.Admin.Persistence/Configurations/DatasetConfiguration.cs` — EF config for both.
- Modify `bpm-admin-svc/src/Bpm.Admin.Persistence/AdminDbContext.cs` — add two `DbSet`s.
- Create `bpm-admin-svc/src/Bpm.Admin.Application/Datasets/IDatasetService.cs` — interface + DTOs/requests.
- Create `bpm-admin-svc/src/Bpm.Admin.Persistence/Datasets/DatasetService.cs` — implementation.
- Create `bpm-admin-svc/src/Bpm.Admin.Api/Controllers/DatasetsController.cs` — CRUD endpoints.
- Modify `bpm-admin-svc/src/Bpm.Admin.Api/Program.cs` — register `IDatasetService`.
- Modify `bpm-admin-svc/src/Bpm.Admin.Persistence/Seed/Seeder.cs` — seed a demo `tw-regions` dataset.
- New migration under `bpm-admin-svc/src/Bpm.Admin.Persistence/Migrations/`.

**bpm-svc (reads + resolves):**
- Create `bpm-svc/src/Persistence/SharedIdentity/SharedDataset.cs` — `SharedDataset` + `SharedDatasetRow` read POCOs.
- Create `bpm-svc/src/Persistence/Configurations/SharedIdentity/SharedDatasetConfiguration.cs` — maps `Admin_Datasets`/`Admin_DatasetRows`, `ExcludeFromMigrations`.
- Modify `bpm-svc/src/Persistence/AppDbContext.cs` — add two `DbSet`s.
- Create `bpm-svc/src/Application/Datasets/DatasetResolutionService.cs` + `IDatasetResolutionService.cs` — the filter/distinct/group/sort core.
- Create `bpm-svc/src/Api/Datasets/DatasetsController.cs` — `POST /api/datasets/resolve`.
- Modify `bpm-svc/src/Api/Program.cs` — register `IDatasetResolutionService`.
- Tests: `bpm-svc/tests/Bpm.Tests/Application/Datasets/DatasetResolutionServiceTests.cs`, `bpm-svc/tests/Bpm.Tests/Api/Datasets/DatasetsControllerTests.cs`.

**bpm-ui (consumes):**
- Create `bpm-ui/src/lib/api/datasets.ts` — `resolveDataset(binding, parentValue)` client.
- Create `bpm-ui/src/components/ui/DatasetSelect.tsx` — the primitive (lead-owned).
- Create `bpm-ui/src/screens/DatasetDemo.tsx` + a dev-only route — proves 縣市→行政區 end-to-end without touching a production flow.
- Modify `bpm-ui/src/router.tsx` — add the dev-only demo route.

**bpm-admin-ui (edits):**
- Modify `bpm-admin-ui/package.json` — add `@tanstack/react-table`.
- Create `bpm-admin-ui/src/flowcook/api/datasets.ts` — typed client.
- Create `bpm-admin-ui/src/flowcook/pages/DatasetsPage.tsx` — list + column editor + row grid.
- Modify `bpm-admin-ui/src/flowcook/Root.tsx` — add the route/nav entry.

---

## PHASE 1 — admin-svc: Dataset domain

### Task 1: Dataset + DatasetRow domain entities

**Files:**
- Create: `bpm-admin-svc/src/Bpm.Admin.Domain/Datasets/Dataset.cs`

- [ ] **Step 1: Write the entities**

```csharp
using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Datasets;

/// A customer-maintained reference table. Columns are stored as a JSON TEXT
/// blob (repo convention: no EF Owned types). Rows live in DatasetRow.
public class Dataset : ISoftDeletable
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;          // stable slug, e.g. "tw-regions"
    public string Name { get; set; } = string.Empty;         // display label
    public string? Description { get; set; }
    public string ColumnsJson { get; set; } = "[]";          // [{"key":"city","label":"縣市","type":"text"}]
    public bool IsActive { get; set; } = true;               // dataset-level enable/disable
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }                 // soft delete (ISoftDeletable)
}

/// One row of a Dataset. Cells = columnKey -> value, stored as JSON TEXT.
public class DatasetRow : ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public string CellsJson { get; set; } = "{}";            // {"city":"台北市","district":"大安區"}
    public bool IsActive { get; set; } = true;               // deactivate-not-delete for history
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

> Note the two distinct flags: `DeletedAt` (soft-delete, hidden by the global query filter) vs `IsActive` (the row still exists & is queryable for history, but is excluded from *new* dropdowns). Resolution filters on `IsActive`; admin "deactivate" toggles `IsActive`; admin "delete" sets `DeletedAt`.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build bpm-admin-svc/src/Bpm.Admin.Domain/Bpm.Admin.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add bpm-admin-svc/src/Bpm.Admin.Domain/Datasets/Dataset.cs
git commit -m "feat(admin-domain): Dataset + DatasetRow entities"
```

---

### Task 2: EF configuration + DbSets + migration

**Files:**
- Create: `bpm-admin-svc/src/Bpm.Admin.Persistence/Configurations/DatasetConfiguration.cs`
- Modify: `bpm-admin-svc/src/Bpm.Admin.Persistence/AdminDbContext.cs`

- [ ] **Step 1: Write the EF configurations**

```csharp
using Bpm.Admin.Domain.Datasets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Admin.Persistence.Configurations;

public class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
{
    public void Configure(EntityTypeBuilder<Dataset> b)
    {
        b.ToTable("Datasets");                       // -> Admin_Datasets via ApplyAdminTablePrefix
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(60);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.ColumnsJson).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();
    }
}

public class DatasetRowConfiguration : IEntityTypeConfiguration<DatasetRow>
{
    public void Configure(EntityTypeBuilder<DatasetRow> b)
    {
        b.ToTable("DatasetRows");                    // -> Admin_DatasetRows
        b.HasKey(x => x.Id);
        b.Property(x => x.CellsJson).IsRequired();
        b.HasIndex(x => x.DatasetId);
        b.HasIndex(x => new { x.DatasetId, x.SortOrder });
    }
}
```

- [ ] **Step 2: Add DbSets to AdminDbContext**

In `AdminDbContext.cs`, alongside the other expression-bodied DbSets (near `public DbSet<FlowGroup> FlowGroups => Set<FlowGroup>();`), add:

```csharp
public DbSet<Dataset> Datasets => Set<Dataset>();
public DbSet<DatasetRow> DatasetRows => Set<DatasetRow>();
```

(Configs are auto-applied by the existing `ApplyConfigurationsFromAssembly`; the `Admin_` prefix and the `ISoftDeletable` global filter are applied automatically.)

- [ ] **Step 3: Generate the migration (Postgres provider for prod schema)**

Run:
```bash
cd bpm-admin-svc/src/Bpm.Admin.Api
BPM_DB_PROVIDER=postgres dotnet ef migrations add AddDatasets -p ../Bpm.Admin.Persistence -s .
```
Expected: a new migration under `Bpm.Admin.Persistence/Migrations/` creating `Admin_Datasets` + `Admin_DatasetRows`, plus a refreshed `AdminDbContextModelSnapshot.cs`.

- [ ] **Step 4: Apply locally and verify build**

Run:
```bash
dotnet ef database update -p ../Bpm.Admin.Persistence -s .
dotnet build
```
Expected: tables created; Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add bpm-admin-svc/src/Bpm.Admin.Persistence/Configurations/DatasetConfiguration.cs \
        bpm-admin-svc/src/Bpm.Admin.Persistence/AdminDbContext.cs \
        bpm-admin-svc/src/Bpm.Admin.Persistence/Migrations/
git commit -m "feat(admin-persistence): EF config + migration for Datasets"
```

---

### Task 3: IDatasetService + DatasetService (CRUD + activate/deactivate)

**Files:**
- Create: `bpm-admin-svc/src/Bpm.Admin.Application/Datasets/IDatasetService.cs`
- Create: `bpm-admin-svc/src/Bpm.Admin.Persistence/Datasets/DatasetService.cs`
- Test: `bpm-admin-svc/tests/Bpm.Admin.Tests/Datasets/DatasetServiceTests.cs` (create the test project folder if absent; mirror the existing admin test project — if none exists, place the test under `bpm-svc/tests/Bpm.Tests/` is NOT correct; admin-svc tests live in its own project. If admin-svc has no test project yet, skip the xUnit step here and rely on the bpm-svc resolution tests in Task 7 + the controller smoke in Task 4. Check `ls bpm-admin-svc` for a `tests/` dir first.)

> **Pre-step:** run `ls bpm-admin-svc/tests 2>/dev/null || echo NONE`. If `NONE`, admin-svc has no xUnit project; do the interface+impl (steps 3-5) and skip the unit-test steps 1-2 (the service is exercised end-to-end by the bpm-svc resolution tests + manual API check). If a tests project exists, write the test.

- [ ] **Step 1 (if admin tests project exists): Write the failing service test**

```csharp
using System.Text.Json;
using Bpm.Admin.Application.Datasets;
// ... usings to construct AdminDbContext on in-memory sqlite + StubClock + a no-op IAuditLogger

public class DatasetServiceTests
{
    [Fact]
    public async Task CreateDataset_then_AddRow_persists_and_lists()
    {
        using var ctx = NewInMemoryAdminDb();
        var svc = new DatasetService(ctx, new StubClock(), new NoopAudit());

        var ds = await svc.CreateAsync(new CreateDatasetRequest(
            "tw-regions", "台灣行政區劃", null,
            new[] { new DatasetColumnDef("city", "縣市", "text"),
                    new DatasetColumnDef("district", "行政區", "text") }), null);

        var row = await svc.AddRowAsync(ds.Id, new AddRowRequest(
            new Dictionary<string,string> { ["city"]="台北市", ["district"]="大安區" }), null);

        var rows = await svc.ListRowsAsync(ds.Id);
        Assert.Single(rows);
        Assert.Equal("大安區", rows[0].Cells["district"]);
        Assert.True(rows[0].IsActive);
    }

    [Fact]
    public async Task DuplicateKey_throws()
    {
        using var ctx = NewInMemoryAdminDb();
        var svc = new DatasetService(ctx, new StubClock(), new NoopAudit());
        await svc.CreateAsync(new CreateDatasetRequest("k","A",null, System.Array.Empty<DatasetColumnDef>()), null);
        await Assert.ThrowsAsync<DatasetException>(() =>
            svc.CreateAsync(new CreateDatasetRequest("k","B",null, System.Array.Empty<DatasetColumnDef>()), null));
    }
}
```

- [ ] **Step 2 (if tests exist): Run to verify it fails**

Run: `dotnet test bpm-admin-svc/tests/Bpm.Admin.Tests --filter DatasetServiceTests`
Expected: FAIL (DatasetService / types not defined).

- [ ] **Step 3: Write the interface + DTOs**

`IDatasetService.cs`:
```csharp
namespace Bpm.Admin.Application.Datasets;

public sealed class DatasetException(string message) : Exception(message);

public record DatasetColumnDef(string Key, string Label, string Type);
public record DatasetDto(Guid Id, string Key, string Name, string? Description,
    IReadOnlyList<DatasetColumnDef> Columns, bool IsActive, int RowCount);
public record DatasetRowDto(Guid Id, Guid DatasetId,
    IReadOnlyDictionary<string,string> Cells, bool IsActive, int SortOrder);

public record CreateDatasetRequest(string Key, string Name, string? Description, IReadOnlyList<DatasetColumnDef> Columns);
public record UpdateDatasetRequest(string? Name, string? Description, IReadOnlyList<DatasetColumnDef>? Columns, bool? IsActive);
public record AddRowRequest(IReadOnlyDictionary<string,string> Cells);
public record UpdateRowRequest(IReadOnlyDictionary<string,string>? Cells, bool? IsActive, int? SortOrder);

public interface IDatasetService
{
    Task<IReadOnlyList<DatasetDto>> ListAsync(CancellationToken ct = default);
    Task<DatasetDto> CreateAsync(CreateDatasetRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task<DatasetDto> UpdateAsync(Guid id, UpdateDatasetRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid? actorUserId, CancellationToken ct = default);

    Task<IReadOnlyList<DatasetRowDto>> ListRowsAsync(Guid datasetId, CancellationToken ct = default);
    Task<DatasetRowDto> AddRowAsync(Guid datasetId, AddRowRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task<DatasetRowDto> UpdateRowAsync(Guid rowId, UpdateRowRequest req, Guid? actorUserId, CancellationToken ct = default);
    Task DeleteRowAsync(Guid rowId, Guid? actorUserId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the implementation** (mirrors `FlowGroupService` — inject `AdminDbContext`, `IClock`, `IAuditLogger`; JSON via `System.Text.Json`; soft-delete sets `DeletedAt`; audit every mutation)

`DatasetService.cs`:
```csharp
using System.Text.Json;
using Bpm.Admin.Application.Datasets;
using Bpm.Admin.Domain.Datasets;
using Bpm.Admin.Application.Common;   // IClock
using Bpm.Admin.Application.Audit;    // IAuditLogger
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Datasets;

public sealed class DatasetService(AdminDbContext db, IClock clock, IAuditLogger audit) : IDatasetService
{
    public async Task<IReadOnlyList<DatasetDto>> ListAsync(CancellationToken ct = default)
    {
        var sets = await db.Datasets.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        var counts = await db.DatasetRows.AsNoTracking()
            .GroupBy(r => r.DatasetId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return sets.Select(d => ToDto(d, counts.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<DatasetDto> CreateAsync(CreateDatasetRequest req, Guid? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Key)) throw new DatasetException("dataset key required");
        if (await db.Datasets.AnyAsync(d => d.Key == req.Key, ct))
            throw new DatasetException($"dataset key '{req.Key}' already in use");
        var row = new Dataset {
            Id = Guid.NewGuid(), Key = req.Key.Trim(), Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            ColumnsJson = JsonSerializer.Serialize(req.Columns), IsActive = true,
            CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
        db.Datasets.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("dataset_created", "dataset", row.Id.ToString(), actor, null, new { row.Key, row.Name }, ct);
        return ToDto(row, 0);
    }

    public async Task<DatasetDto> UpdateAsync(Guid id, UpdateDatasetRequest req, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new DatasetException("dataset not found");
        if (req.Name is not null) row.Name = req.Name.Trim();
        if (req.Description is not null) row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description;
        if (req.Columns is not null) row.ColumnsJson = JsonSerializer.Serialize(req.Columns);
        if (req.IsActive is not null) row.IsActive = req.IsActive.Value;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("dataset_updated", "dataset", id.ToString(), actor, null, new { row.Name, row.IsActive }, ct);
        var n = await db.DatasetRows.CountAsync(r => r.DatasetId == id, ct);
        return ToDto(row, n);
    }

    public async Task DeleteAsync(Guid id, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new DatasetException("dataset not found");
        row.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("dataset_deleted", "dataset", id.ToString(), actor, null, null, ct);
    }

    public async Task<IReadOnlyList<DatasetRowDto>> ListRowsAsync(Guid datasetId, CancellationToken ct = default)
    {
        var rows = await db.DatasetRows.AsNoTracking().Where(r => r.DatasetId == datasetId)
            .OrderBy(r => r.SortOrder).ToListAsync(ct);
        return rows.Select(ToRowDto).ToList();
    }

    public async Task<DatasetRowDto> AddRowAsync(Guid datasetId, AddRowRequest req, Guid? actor, CancellationToken ct = default)
    {
        if (!await db.Datasets.AnyAsync(d => d.Id == datasetId, ct)) throw new DatasetException("dataset not found");
        var maxOrder = await db.DatasetRows.Where(r => r.DatasetId == datasetId)
            .Select(r => (int?)r.SortOrder).MaxAsync(ct) ?? 0;
        var row = new DatasetRow {
            Id = Guid.NewGuid(), DatasetId = datasetId,
            CellsJson = JsonSerializer.Serialize(req.Cells), IsActive = true, SortOrder = maxOrder + 1,
            CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
        db.DatasetRows.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("dataset_row_added", "dataset_row", row.Id.ToString(), actor, null, new { datasetId }, ct);
        return ToRowDto(row);
    }

    public async Task<DatasetRowDto> UpdateRowAsync(Guid rowId, UpdateRowRequest req, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.DatasetRows.FirstOrDefaultAsync(r => r.Id == rowId, ct) ?? throw new DatasetException("row not found");
        if (req.Cells is not null) row.CellsJson = JsonSerializer.Serialize(req.Cells);
        if (req.IsActive is not null) row.IsActive = req.IsActive.Value;
        if (req.SortOrder is not null) row.SortOrder = req.SortOrder.Value;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("dataset_row_updated", "dataset_row", rowId.ToString(), actor, null, new { row.IsActive }, ct);
        return ToRowDto(row);
    }

    public async Task DeleteRowAsync(Guid rowId, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.DatasetRows.FirstOrDefaultAsync(r => r.Id == rowId, ct) ?? throw new DatasetException("row not found");
        row.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("dataset_row_deleted", "dataset_row", rowId.ToString(), actor, null, null, ct);
    }

    private static DatasetDto ToDto(Dataset d, int rowCount) => new(
        d.Id, d.Key, d.Name, d.Description,
        JsonSerializer.Deserialize<List<DatasetColumnDef>>(d.ColumnsJson) ?? new(), d.IsActive, rowCount);

    private static DatasetRowDto ToRowDto(DatasetRow r) => new(
        r.Id, r.DatasetId,
        JsonSerializer.Deserialize<Dictionary<string,string>>(r.CellsJson) ?? new(), r.IsActive, r.SortOrder);
}
```

> **Note on namespaces:** match the actual ones found in the codebase — `IClock` and `IAuditLogger` live where `FlowGroupService` imports them from. Before writing, open `bpm-admin-svc/src/Bpm.Admin.Persistence/Flows/FlowGroupService.cs` and copy its exact `using` lines for `IClock`/`IAuditLogger`/`AdminDbContext`, plus the exact `LogAsync(...)` signature (argument order). Adjust the `audit.LogAsync(...)` calls above to that signature.

- [ ] **Step 5: Run tests (if present) / build**

Run: `dotnet test bpm-admin-svc/tests/Bpm.Admin.Tests --filter DatasetServiceTests` (if the project exists)
Expected: PASS.
Otherwise: `dotnet build bpm-admin-svc/src/Bpm.Admin.Persistence` → Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add bpm-admin-svc/src/Bpm.Admin.Application/Datasets/ bpm-admin-svc/src/Bpm.Admin.Persistence/Datasets/
git commit -m "feat(admin): IDatasetService + DatasetService (dataset/row CRUD)"
```

---

### Task 4: DatasetsController + DI

**Files:**
- Create: `bpm-admin-svc/src/Bpm.Admin.Api/Controllers/DatasetsController.cs`
- Modify: `bpm-admin-svc/src/Bpm.Admin.Api/Program.cs`

- [ ] **Step 1: Write the controller** (mirror `FlowGroupsController`; gate writes with the `SystemAdmin` policy)

```csharp
using System.Security.Claims;
using Bpm.Admin.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/datasets")]
public sealed class DatasetsController(IDatasetService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DatasetDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:guid}/rows")]
    public async Task<ActionResult<IEnumerable<DatasetRowDto>>> Rows(Guid id, CancellationToken ct)
        => Ok(await svc.ListRowsAsync(id, ct));

    [HttpPost, Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetDto>> Create([FromBody] CreateDatasetRequest req, CancellationToken ct)
    { try { return Ok(await svc.CreateAsync(req, Actor(), ct)); } catch (DatasetException e) { return BadRequest(e.Message); } }

    [HttpPut("{id:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetDto>> Update(Guid id, [FromBody] UpdateDatasetRequest req, CancellationToken ct)
    { try { return Ok(await svc.UpdateAsync(id, req, Actor(), ct)); } catch (DatasetException e) { return NotFound(e.Message); } }

    [HttpDelete("{id:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { try { await svc.DeleteAsync(id, Actor(), ct); return NoContent(); } catch (DatasetException e) { return NotFound(e.Message); } }

    [HttpPost("{id:guid}/rows"), Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetRowDto>> AddRow(Guid id, [FromBody] AddRowRequest req, CancellationToken ct)
    { try { return Ok(await svc.AddRowAsync(id, req, Actor(), ct)); } catch (DatasetException e) { return BadRequest(e.Message); } }

    [HttpPut("rows/{rowId:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetRowDto>> UpdateRow(Guid rowId, [FromBody] UpdateRowRequest req, CancellationToken ct)
    { try { return Ok(await svc.UpdateRowAsync(rowId, req, Actor(), ct)); } catch (DatasetException e) { return NotFound(e.Message); } }

    [HttpDelete("rows/{rowId:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<IActionResult> DeleteRow(Guid rowId, CancellationToken ct)
    { try { await svc.DeleteRowAsync(rowId, Actor(), ct); return NoContent(); } catch (DatasetException e) { return NotFound(e.Message); } }

    private Guid? Actor()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}
```

- [ ] **Step 2: Register the service in Program.cs**

In `bpm-admin-svc/src/Bpm.Admin.Api/Program.cs`, beside `builder.Services.AddScoped<IFlowGroupService, FlowGroupService>();` add:
```csharp
builder.Services.AddScoped<IDatasetService, Bpm.Admin.Persistence.Datasets.DatasetService>();
```

- [ ] **Step 3: Build + manual API smoke**

Run: `dotnet build bpm-admin-svc/src/Bpm.Admin.Api`
Expected: Build succeeded.

Then boot admin-svc (`cd bpm-admin-svc/src/Bpm.Admin.Api && dotnet run --launch-profile http`), get a SYSTEM_ADMIN token via `/api/auth/login` (alice→no; use jack@acme.example / flowcook2026), and:
```bash
TOK=...   # jack's token
curl -s -X POST localhost:5266/api/datasets -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  -d '{"key":"smoke","name":"Smoke","description":null,"columns":[{"key":"c","label":"C","type":"text"}]}'
curl -s localhost:5266/api/datasets -H "Authorization: Bearer $TOK"
```
Expected: create returns the DatasetDto; list includes it.

- [ ] **Step 4: Commit**

```bash
git add bpm-admin-svc/src/Bpm.Admin.Api/Controllers/DatasetsController.cs bpm-admin-svc/src/Bpm.Admin.Api/Program.cs
git commit -m "feat(admin-api): DatasetsController + DI"
```

---

### Task 5: Seed the demo `tw-regions` dataset

**Files:**
- Modify: `bpm-admin-svc/src/Bpm.Admin.Persistence/Seed/Seeder.cs`

- [ ] **Step 1: Add a seed block** (mirror the Role-seeding loop). Inside `SeedOrgAsync`, after existing seeds:

```csharp
// ── demo dataset: 台灣行政區劃 (cascading 縣市 -> 行政區) ──────────────
if (!ctx.Datasets.Any(d => d.Key == "tw-regions"))
{
    var ds = new Bpm.Admin.Domain.Datasets.Dataset {
        Id = Guid.NewGuid(), Key = "tw-regions", Name = "台灣行政區劃",
        Description = "Demo dataset for cascading 縣市→行政區",
        ColumnsJson = System.Text.Json.JsonSerializer.Serialize(new[] {
            new { key = "city", label = "縣市", type = "text" },
            new { key = "district", label = "行政區", type = "text" } }),
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    ctx.Datasets.Add(ds);
    var pairs = new (string City, string District)[] {
        ("台北市","大安區"), ("台北市","信義區"), ("台北市","中山區"),
        ("新北市","板橋區"), ("新北市","三重區"), ("新北市","新莊區"),
        ("台中市","西屯區"), ("台中市","北屯區") };
    var order = 0;
    foreach (var (city, district) in pairs)
        ctx.DatasetRows.Add(new Bpm.Admin.Domain.Datasets.DatasetRow {
            Id = Guid.NewGuid(), DatasetId = ds.Id,
            CellsJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string,string> { ["city"]=city, ["district"]=district }),
            IsActive = true, SortOrder = ++order, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
}
```

- [ ] **Step 2: Run the seeder + verify**

Run:
```bash
cd bpm-admin-svc/src/Bpm.Admin.SeedCli
FLOWCOOK_ALLOW_SEED=1 dotnet run -- seed --org
```
Then query the API (as in Task 4 step 3): `curl -s localhost:5266/api/datasets -H "Authorization: Bearer $TOK"` shows `tw-regions` with rowCount 8.

- [ ] **Step 3: Commit**

```bash
git add bpm-admin-svc/src/Bpm.Admin.Persistence/Seed/Seeder.cs
git commit -m "feat(admin-seed): demo tw-regions cascading dataset"
```

---

## PHASE 2 — bpm-svc: read mirror + resolve

### Task 6: SharedDataset/SharedDatasetRow read mirror

**Files:**
- Create: `bpm-svc/src/Persistence/SharedIdentity/SharedDataset.cs`
- Create: `bpm-svc/src/Persistence/Configurations/SharedIdentity/SharedDatasetConfiguration.cs`
- Modify: `bpm-svc/src/Persistence/AppDbContext.cs`

- [ ] **Step 1: Write the read POCOs**

```csharp
namespace Bpm.Persistence.SharedIdentity;

/// Read-model mirror of admin-svc's Dataset. Schema owned by admin (ExcludeFromMigrations).
public sealed class SharedDataset
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ColumnsJson { get; set; } = "[]";
    public bool IsActive { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SharedDatasetRow
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public string CellsJson { get; set; } = "{}";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

- [ ] **Step 2: Write the EF config mapping the Admin_ tables, ExcludeFromMigrations**

```csharp
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bpm.Persistence.Configurations.SharedIdentity;

public sealed class SharedDatasetConfiguration : IEntityTypeConfiguration<SharedDataset>
{
    public void Configure(EntityTypeBuilder<SharedDataset> b)
    {
        b.ToTable("Admin_Datasets", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(60);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.ColumnsJson).IsRequired();
    }
}

public sealed class SharedDatasetRowConfiguration : IEntityTypeConfiguration<SharedDatasetRow>
{
    public void Configure(EntityTypeBuilder<SharedDatasetRow> b)
    {
        b.ToTable("Admin_DatasetRows", t => t.ExcludeFromMigrations());
        b.HasKey(x => x.Id);
        b.Property(x => x.CellsJson).IsRequired();
    }
}
```

- [ ] **Step 3: Add DbSets to AppDbContext**

In `bpm-svc/src/Persistence/AppDbContext.cs`, beside the other `Shared*` sets:
```csharp
public DbSet<SharedDataset> SharedDatasets => Set<SharedDataset>();
public DbSet<SharedDatasetRow> SharedDatasetRows => Set<SharedDatasetRow>();
```

- [ ] **Step 4: Build (no migration — these are ExcludeFromMigrations)**

Run: `dotnet build bpm-svc/src/Persistence`
Expected: Build succeeded. Confirm **no** new bpm-svc migration was generated.

- [ ] **Step 5: Commit**

```bash
git add bpm-svc/src/Persistence/SharedIdentity/SharedDataset.cs \
        bpm-svc/src/Persistence/Configurations/SharedIdentity/SharedDatasetConfiguration.cs \
        bpm-svc/src/Persistence/AppDbContext.cs
git commit -m "feat(bpm-persistence): SharedDataset read mirror (ExcludeFromMigrations)"
```

---

### Task 7: DatasetResolutionService — filter / distinct / group / sort (the core, TDD)

**Files:**
- Create: `bpm-svc/src/Application/Datasets/IDatasetResolutionService.cs`
- Create: `bpm-svc/src/Application/Datasets/DatasetResolutionService.cs`
- Test: `bpm-svc/tests/Bpm.Tests/Application/Datasets/DatasetResolutionServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Bpm.Application.Datasets;
using Bpm.Persistence;
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
        db.SharedDatasets.Add(new SharedDataset { Id = dsId, Key = "tw-regions", Name = "R",
            ColumnsJson = "[]", IsActive = true });
        void Row(string city, string district, bool active = true, int order = 0) =>
            db.SharedDatasetRows.Add(new SharedDatasetRow { Id = Guid.NewGuid(), DatasetId = dsId,
                CellsJson = JsonSerializer.Serialize(new Dictionary<string,string>{["city"]=city,["district"]=district}),
                IsActive = active, SortOrder = order });
        Row("台北市","大安區",true,1); Row("台北市","信義區",true,2);
        Row("新北市","板橋區",true,3); Row("新北市","板橋區",true,4);   // dup district under same city -> distinct test
        Row("台中市","西屯區",false,5);                                 // inactive -> excluded
        db.SaveChanges();
    }

    private DatasetResolutionService Svc() => new DatasetResolutionService(new AppDbContext(_opts));

    [Fact]
    public async Task Filter_by_parent_returns_only_matching_rows()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions","district",null,"city","台北市",false,null,null), default);
        Assert.Equal(new[]{"大安區","信義區"}, res.Select(o => o.Value).ToArray());
    }

    [Fact]
    public async Task Distinct_dedupes_repeated_values()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions","district",null,"city","新北市",true,null,null), default);
        Assert.Equal(new[]{"板橋區"}, res.Select(o => o.Value).ToArray());   // two rows collapse to one
    }

    [Fact]
    public async Task Inactive_rows_excluded()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions","district",null,"city","台中市",false,null,null), default);
        Assert.Empty(res);
    }

    [Fact]
    public async Task Missing_filter_value_with_filter_column_returns_empty()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions","district",null,"city",null,false,null,null), default);
        Assert.Empty(res);   // cascading child with no parent selected
    }

    [Fact]
    public async Task No_filter_column_returns_all_active_distinct_cities()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions","city",null,null,null,true,null,null), default);
        Assert.Equal(new[]{"台北市","新北市"}, res.Select(o => o.Value).ToArray());   // 台中市 inactive
    }

    [Fact]
    public async Task Label_defaults_to_value_column_and_group_populates()
    {
        var res = await Svc().ResolveAsync(new ResolveRequest("tw-regions","district",null,null,null,false,"city",null), default);
        Assert.All(res, o => Assert.Equal(o.Value, o.Label));   // label defaults to value
        Assert.Contains(res, o => o.Group == "台北市");
    }

    public void Dispose() => _conn.Dispose();
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test bpm-svc/tests/Bpm.Tests --filter DatasetResolutionServiceTests`
Expected: FAIL (types not defined).

- [ ] **Step 3: Write the interface + request/result records**

`IDatasetResolutionService.cs`:
```csharp
namespace Bpm.Application.Datasets;

/// A declarative option query over a dataset (the form field's binding + the
/// parent's selected value). filterValue null + filterColumn set => empty (child
/// not ready). distinct dedupes by (value,label,group). sortColumn null => row SortOrder.
public record ResolveRequest(
    string DatasetKey, string ValueColumn, string? LabelColumn,
    string? FilterColumn, string? FilterValue,
    bool Distinct, string? GroupByColumn, string? SortByColumn);

public record DatasetOption(string Value, string Label, string? Group);

public interface IDatasetResolutionService
{
    Task<IReadOnlyList<DatasetOption>> ResolveAsync(ResolveRequest req, CancellationToken ct);
}
```

- [ ] **Step 4: Write the implementation** (load active rows, parse cells, apply filter→sort→project→distinct→group, all in memory)

`DatasetResolutionService.cs`:
```csharp
using System.Text.Json;
using Bpm.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Datasets;

public sealed class DatasetResolutionService(AppDbContext db) : IDatasetResolutionService
{
    public async Task<IReadOnlyList<DatasetOption>> ResolveAsync(ResolveRequest req, CancellationToken ct)
    {
        // cascading child with no parent value selected yet -> no options
        if (!string.IsNullOrEmpty(req.FilterColumn) && string.IsNullOrEmpty(req.FilterValue))
            return Array.Empty<DatasetOption>();

        var ds = await db.SharedDatasets.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Key == req.DatasetKey && d.IsActive && d.DeletedAt == null, ct);
        if (ds is null) return Array.Empty<DatasetOption>();

        var rows = await db.SharedDatasetRows.AsNoTracking()
            .Where(r => r.DatasetId == ds.Id && r.IsActive && r.DeletedAt == null)
            .OrderBy(r => r.SortOrder).ToListAsync(ct);

        var labelCol = string.IsNullOrEmpty(req.LabelColumn) ? req.ValueColumn : req.LabelColumn!;

        var projected = rows
            .Select(r => JsonSerializer.Deserialize<Dictionary<string, string>>(r.CellsJson) ?? new())
            .Where(cells => string.IsNullOrEmpty(req.FilterColumn)
                            || (cells.TryGetValue(req.FilterColumn!, out var fv) && fv == req.FilterValue))
            .Select(cells => new DatasetOption(
                cells.GetValueOrDefault(req.ValueColumn, ""),
                cells.GetValueOrDefault(labelCol, cells.GetValueOrDefault(req.ValueColumn, "")),
                string.IsNullOrEmpty(req.GroupByColumn) ? null : cells.GetValueOrDefault(req.GroupByColumn!)))
            .Where(o => o.Value.Length > 0);

        if (!string.IsNullOrEmpty(req.SortByColumn))
            projected = projected.OrderBy(o => o.Label, StringComparer.Ordinal);

        if (req.Distinct)
            projected = projected
                .GroupBy(o => (o.Value, o.Label, o.Group))
                .Select(g => g.First());

        return projected.ToList();
    }
}
```

> Sort note: `SortByColumn` here sorts by the resolved label (sufficient for phase 1; row `SortOrder` is the default order otherwise). If a later need arises to sort by an arbitrary cell column independent of value/label, extend `DatasetOption` to carry the sort key — not now (YAGNI).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test bpm-svc/tests/Bpm.Tests --filter DatasetResolutionServiceTests`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
git add bpm-svc/src/Application/Datasets/ bpm-svc/tests/Bpm.Tests/Application/Datasets/
git commit -m "feat(bpm-app): DatasetResolutionService (filter/distinct/group/sort) + tests"
```

---

### Task 8: bpm-svc DatasetsController — POST /api/datasets/resolve

**Files:**
- Create: `bpm-svc/src/Api/Datasets/DatasetsController.cs`
- Modify: `bpm-svc/src/Api/Program.cs`
- Test: `bpm-svc/tests/Bpm.Tests/Api/Datasets/DatasetsControllerTests.cs`

- [ ] **Step 1: Write the failing controller test** (new the controller with a fake principal — no WebApplicationFactory)

```csharp
using System.Security.Claims;
using Bpm.Api.Datasets;
using Bpm.Application.Datasets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Bpm.Tests.Api.Datasets;

public class DatasetsControllerTests
{
    private sealed class FakeResolver : IDatasetResolutionService
    {
        public Task<IReadOnlyList<DatasetOption>> ResolveAsync(ResolveRequest req, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DatasetOption>>(new[] { new DatasetOption("大安區","大安區",null) });
    }

    [Fact]
    public async Task Resolve_returns_options()
    {
        var c = new DatasetsController(new FakeResolver());
        var identity = new ClaimsIdentity(new[] { new Claim("sub", Guid.NewGuid().ToString()) }, "test", "sub", "roles");
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };

        var result = await c.Resolve(new ResolveRequest("tw-regions","district",null,"city","台北市",false,null,null), default);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsAssignableFrom<IReadOnlyList<DatasetOption>>(ok.Value);
        Assert.Single(body);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test bpm-svc/tests/Bpm.Tests --filter DatasetsControllerTests`
Expected: FAIL (DatasetsController not defined).

- [ ] **Step 3: Write the controller** (explicit `[Authorize]`, `BpmControllerBase`)

```csharp
using Bpm.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Datasets;

[ApiController]
[Authorize]
[Route("api/datasets")]
public sealed class DatasetsController(IDatasetResolutionService resolver) : BpmControllerBase
{
    [HttpPost("resolve")]
    public async Task<ActionResult<IReadOnlyList<DatasetOption>>> Resolve([FromBody] ResolveRequest req, CancellationToken ct)
        => Ok(await resolver.ResolveAsync(req, ct));
}
```

> Confirm `BpmControllerBase`'s namespace from `FlowRegistryController.cs` and add the matching `using`.

- [ ] **Step 4: Register the service in Program.cs**

In `bpm-svc/src/Api/Program.cs`, beside the other `AddScoped` lines:
```csharp
builder.Services.AddScoped<IDatasetResolutionService, DatasetResolutionService>();
```
(add `using Bpm.Application.Datasets;`)

- [ ] **Step 5: Run tests + build**

Run: `dotnet test bpm-svc/tests/Bpm.Tests --filter DatasetsControllerTests` → PASS
Run: `dotnet build bpm-svc/src/Api` → Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add bpm-svc/src/Api/Datasets/ bpm-svc/src/Api/Program.cs bpm-svc/tests/Bpm.Tests/Api/Datasets/
git commit -m "feat(bpm-api): POST /api/datasets/resolve + test"
```

---

## PHASE 3 — bpm-ui: DatasetSelect primitive + demo

> Frontend has no JS test runner. Each task's "test" = `npx tsc -p tsconfig.app.json --noEmit` + a manual browser check via chrome-devtools.

### Task 9: dataset client + DatasetSelect primitive

**Files:**
- Create: `bpm-ui/src/lib/api/datasets.ts`
- Create: `bpm-ui/src/components/ui/DatasetSelect.tsx`

- [ ] **Step 1: Write the client** (uses the existing `apiFetch` raw-Response helper)

```ts
import { apiFetch } from '@/lib/apiFetch'

export interface DatasetBinding {
  datasetKey: string
  valueColumn: string
  labelColumn?: string
  filterByColumn?: string
  distinct?: boolean
  groupByColumn?: string
  sortByColumn?: string
}

export interface DatasetOption { value: string; label: string; group?: string | null }

export async function resolveDataset(binding: DatasetBinding, parentValue?: string): Promise<DatasetOption[]> {
  const res = await apiFetch('/api/datasets/resolve', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      datasetKey: binding.datasetKey,
      valueColumn: binding.valueColumn,
      labelColumn: binding.labelColumn ?? null,
      filterColumn: binding.filterByColumn ?? null,
      filterValue: parentValue ?? null,
      distinct: binding.distinct ?? false,
      groupByColumn: binding.groupByColumn ?? null,
      sortByColumn: binding.sortByColumn ?? null,
    }),
  })
  if (!res.ok) return []
  return (await res.json()) as DatasetOption[]
}
```

- [ ] **Step 2: Write the DatasetSelect primitive** (wraps the existing styled `Select`; cascading via `parentValue`; renders `<optgroup>` when grouped; returns value + label on change for snapshotting)

```tsx
import { useEffect, useState } from 'react'
import { Select } from '@/components/ui/form'
import { resolveDataset, type DatasetBinding, type DatasetOption } from '@/lib/api/datasets'

interface Props {
  binding: DatasetBinding
  value: string
  onChange: (value: string, label: string) => void
  parentValue?: string          // cascading: the parent field's selected value
  disabled?: boolean
  placeholder?: string
}

export function DatasetSelect({ binding, value, onChange, parentValue, disabled, placeholder = '請選擇' }: Props) {
  const [options, setOptions] = useState<DatasetOption[]>([])
  const [loading, setLoading] = useState(false)

  // re-resolve whenever the binding's parent value changes (cascading)
  useEffect(() => {
    let live = true
    setLoading(true)
    resolveDataset(binding, parentValue)
      .then(opts => { if (live) setOptions(opts) })
      .finally(() => { if (live) setLoading(false) })
    return () => { live = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [binding.datasetKey, binding.valueColumn, binding.filterByColumn, parentValue])

  // cascading child is disabled until its parent has a value
  const isChildWaiting = Boolean(binding.filterByColumn) && !parentValue
  const labelFor = (v: string) => options.find(o => o.value === v)?.label ?? v

  const grouped = binding.groupByColumn
    ? Array.from(new Set(options.map(o => o.group ?? ''))).map(g => ({
        group: g, items: options.filter(o => (o.group ?? '') === g),
      }))
    : null

  return (
    <Select
      value={value}
      disabled={disabled || isChildWaiting || loading}
      onChange={e => onChange(e.target.value, labelFor(e.target.value))}
    >
      <option value="">{isChildWaiting ? '請先選擇上一層' : loading ? '載入中…' : placeholder}</option>
      {grouped
        ? grouped.map(({ group, items }) =>
            <optgroup key={group || '—'} label={group || '—'}>
              {items.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </optgroup>)
        : options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
    </Select>
  )
}
```

- [ ] **Step 3: Typecheck**

Run: `cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add bpm-ui/src/lib/api/datasets.ts bpm-ui/src/components/ui/DatasetSelect.tsx
git commit -m "feat(bpm-ui): DatasetSelect primitive + resolve client"
```

---

### Task 10: dev-only demo screen (proves 縣市→行政區 end-to-end)

**Files:**
- Create: `bpm-ui/src/screens/DatasetDemo.tsx`
- Modify: `bpm-ui/src/router.tsx`

- [ ] **Step 1: Write the demo screen** (two DatasetSelects; the district one cascades off city)

```tsx
import { useState } from 'react'
import { DatasetSelect } from '@/components/ui/DatasetSelect'
import { Field, FieldLabel } from '@/components/ui/form'

export default function DatasetDemo() {
  const [city, setCity] = useState('')
  const [cityLabel, setCityLabel] = useState('')
  const [district, setDistrict] = useState('')
  const [districtLabel, setDistrictLabel] = useState('')

  return (
    <div className="mx-auto max-w-md space-y-4 p-6">
      <h1 className="text-lg font-semibold text-ink">Dataset demo — 縣市 → 行政區</h1>
      <Field>
        <FieldLabel>縣市</FieldLabel>
        <DatasetSelect
          binding={{ datasetKey: 'tw-regions', valueColumn: 'city', distinct: true }}
          value={city}
          onChange={(v, l) => { setCity(v); setCityLabel(l); setDistrict(''); setDistrictLabel('') }}
        />
      </Field>
      <Field>
        <FieldLabel>行政區</FieldLabel>
        <DatasetSelect
          binding={{ datasetKey: 'tw-regions', valueColumn: 'district', filterByColumn: 'city' }}
          parentValue={city}
          value={district}
          onChange={(v, l) => { setDistrict(v); setDistrictLabel(l) }}
        />
      </Field>
      <pre className="rounded bg-slate-50 p-3 text-xs text-ink-muted">
        {JSON.stringify({ city, cityLabel, district, districtLabel }, null, 2)}
      </pre>
    </div>
  )
}
```

- [ ] **Step 2: Add a dev-only route** in `router.tsx` (guard so it never ships in a prod build)

```tsx
// inside the route list, alongside other routes:
...(import.meta.env.DEV ? [{ path: '/dataset-demo', element: <DatasetDemo /> }] : []),
```
Add the lazy/static import at the top: `import DatasetDemo from '@/screens/DatasetDemo'` (or follow the file's existing import style).

- [ ] **Step 3: Typecheck + manual browser verification**

Run: `cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit` → no errors.

Manual (boot bpm-svc :5290 + bpm-ui :5173, seeded DB from Task 5):
- Navigate to `/dataset-demo`, log in.
- 縣市 dropdown shows **台北市 / 新北市 / 台中市** (distinct cities).
- 行政區 is disabled showing "請先選擇上一層" until a city is picked.
- Pick 台北市 → 行政區 shows 大安區 / 信義區 / 中山區 only.
- Switch to 新北市 → 行政區 resets and shows 板橋區 / 三重區 / 新莊區.
- The JSON dump shows both value and label captured.

- [ ] **Step 4: Commit**

```bash
git add bpm-ui/src/screens/DatasetDemo.tsx bpm-ui/src/router.tsx
git commit -m "feat(bpm-ui): dev-only dataset cascading demo screen"
```

---

## PHASE 4 — bpm-admin-ui: Datasets editor

### Task 11: TanStack Table dep + typed datasets client

**Files:**
- Modify: `bpm-admin-ui/package.json`
- Create: `bpm-admin-ui/src/flowcook/api/datasets.ts`

- [ ] **Step 1: Add the dependency**

Run: `cd bpm-admin-ui && npm install @tanstack/react-table`
Expected: `@tanstack/react-table` added to `package.json` dependencies; `package-lock.json` updated.

- [ ] **Step 2: Write the typed client** (mirror `flowGroups.ts`; uses admin-ui's `api<T>`)

```ts
import { api } from '@/flowcook/api'

export interface DatasetColumnDef { key: string; label: string; type: string }
export interface DatasetDto { id: string; key: string; name: string; description: string | null; columns: DatasetColumnDef[]; isActive: boolean; rowCount: number }
export interface DatasetRowDto { id: string; datasetId: string; cells: Record<string,string>; isActive: boolean; sortOrder: number }

export interface CreateDatasetRequest { key: string; name: string; description: string | null; columns: DatasetColumnDef[] }
export interface UpdateDatasetRequest { name?: string; description?: string | null; columns?: DatasetColumnDef[]; isActive?: boolean }
export interface AddRowRequest { cells: Record<string,string> }
export interface UpdateRowRequest { cells?: Record<string,string>; isActive?: boolean; sortOrder?: number }

export const listDatasets = () => api<DatasetDto[]>('/api/datasets')
export const createDataset = (req: CreateDatasetRequest) => api<DatasetDto>('/api/datasets', { method: 'POST', json: req })
export const updateDataset = (id: string, req: UpdateDatasetRequest) => api<DatasetDto>(`/api/datasets/${id}`, { method: 'PUT', json: req })
export const deleteDataset = (id: string) => api<void>(`/api/datasets/${id}`, { method: 'DELETE' })

export const listRows = (id: string) => api<DatasetRowDto[]>(`/api/datasets/${id}/rows`)
export const addRow = (id: string, req: AddRowRequest) => api<DatasetRowDto>(`/api/datasets/${id}/rows`, { method: 'POST', json: req })
export const updateRow = (rowId: string, req: UpdateRowRequest) => api<DatasetRowDto>(`/api/datasets/rows/${rowId}`, { method: 'PUT', json: req })
export const deleteRow = (rowId: string) => api<void>(`/api/datasets/rows/${rowId}`, { method: 'DELETE' })
```

- [ ] **Step 3: Typecheck**

Run: `cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit`
Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add bpm-admin-ui/package.json bpm-admin-ui/package-lock.json bpm-admin-ui/src/flowcook/api/datasets.ts
git commit -m "feat(admin-ui): @tanstack/react-table + typed datasets client"
```

---

### Task 12: Datasets page (list + column editor + row grid)

**Files:**
- Create: `bpm-admin-ui/src/flowcook/pages/DatasetsPage.tsx`
- Modify: `bpm-admin-ui/src/flowcook/Root.tsx`

- [ ] **Step 1: Write the page** (master list of datasets on the left; on select, a TanStack Table of rows on the right with add / inline edit / deactivate). Match the existing visual tokens (`border-rule`, `bg-card`, `text-ink`, `text-ink-muted`, `bg-primary`) and use `data-testid` like RolesTab.

```tsx
import { useEffect, useMemo, useState } from 'react'
import {
  useReactTable, getCoreRowModel, flexRender, type ColumnDef,
} from '@tanstack/react-table'
import { Plus, Trash2, Power } from 'lucide-react'
import {
  listDatasets, listRows, addRow, updateRow, deleteRow,
  type DatasetDto, type DatasetRowDto,
} from '@/flowcook/api/datasets'

export default function DatasetsPage() {
  const [datasets, setDatasets] = useState<DatasetDto[]>([])
  const [selected, setSelected] = useState<DatasetDto | null>(null)
  const [rows, setRows] = useState<DatasetRowDto[]>([])

  useEffect(() => { listDatasets().then(setDatasets) }, [])
  useEffect(() => { if (selected) listRows(selected.id).then(setRows); else setRows([]) }, [selected])

  async function setCell(row: DatasetRowDto, colKey: string, val: string) {
    const next = { ...row.cells, [colKey]: val }
    const saved = await updateRow(row.id, { cells: next })
    setRows(rs => rs.map(r => r.id === saved.id ? saved : r))
  }
  async function toggleActive(row: DatasetRowDto) {
    const saved = await updateRow(row.id, { isActive: !row.isActive })
    setRows(rs => rs.map(r => r.id === saved.id ? saved : r))
  }
  async function removeRow(row: DatasetRowDto) {
    await deleteRow(row.id); setRows(rs => rs.filter(r => r.id !== row.id))
  }
  async function newRow() {
    if (!selected) return
    const blank = Object.fromEntries(selected.columns.map(c => [c.key, '']))
    const created = await addRow(selected.id, { cells: blank })
    setRows(rs => [...rs, created])
  }

  const columns = useMemo<ColumnDef<DatasetRowDto>[]>(() => {
    const cols: ColumnDef<DatasetRowDto>[] = (selected?.columns ?? []).map(c => ({
      id: c.key, header: c.label,
      cell: ({ row }) => (
        <input
          defaultValue={row.original.cells[c.key] ?? ''}
          onBlur={e => setCell(row.original, c.key, e.target.value)}
          className="w-full rounded border border-rule bg-card px-2 py-1 text-sm text-ink focus:outline-none focus:ring-1 focus:ring-primary"
          data-testid={`cell-${c.key}-${row.index}`}
        />
      ),
    }))
    cols.push({
      id: '_actions', header: '',
      cell: ({ row }) => (
        <div className="flex gap-1">
          <button onClick={() => toggleActive(row.original)} title={row.original.isActive ? '停用' : '啟用'}
            className={row.original.isActive ? 'text-ink-muted' : 'text-amber-600'} data-testid={`toggle-${row.index}`}>
            <Power className="h-4 w-4" />
          </button>
          <button onClick={() => removeRow(row.original)} title="刪除" className="text-red-500" data-testid={`del-${row.index}`}>
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      ),
    })
    return cols
  }, [selected, rows])

  const table = useReactTable({ data: rows, columns, getCoreRowModel: getCoreRowModel() })

  return (
    <div className="grid grid-cols-12 gap-4 p-4">
      <aside className="col-span-3 space-y-1">
        <h2 className="mb-2 text-sm font-semibold text-ink">資料集 / Datasets</h2>
        {datasets.map(d => (
          <button key={d.id} onClick={() => setSelected(d)} data-testid={`dataset-${d.key}`}
            className={`block w-full rounded px-3 py-2 text-left text-sm ${selected?.id === d.id ? 'bg-primary/10 text-ink' : 'text-ink-muted hover:bg-slate-50'}`}>
            {d.name} <span className="text-ink-faint">· {d.rowCount}</span>
          </button>
        ))}
      </aside>

      <section className="col-span-9">
        {!selected ? (
          <p className="text-sm text-ink-muted">選擇一個資料集以編輯內容。</p>
        ) : (
          <>
            <div className="mb-3 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-ink">{selected.name}</h3>
              <button onClick={newRow} data-testid="add-row"
                className="inline-flex items-center gap-1 rounded bg-primary px-3 py-1.5 text-sm text-white">
                <Plus className="h-4 w-4" /> 新增列
              </button>
            </div>
            <table className="w-full border-collapse text-sm">
              <thead>
                {table.getHeaderGroups().map(hg => (
                  <tr key={hg.id} className="border-b border-rule text-left text-ink-muted">
                    {hg.headers.map(h => <th key={h.id} className="py-2 pr-3 font-medium">
                      {flexRender(h.column.columnDef.header, h.getContext())}</th>)}
                  </tr>
                ))}
              </thead>
              <tbody>
                {table.getRowModel().rows.map(r => (
                  <tr key={r.id} className={`border-b border-rule ${r.original.isActive ? '' : 'opacity-40'}`}>
                    {r.getVisibleCells().map(cell => <td key={cell.id} className="py-1.5 pr-3">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </section>
    </div>
  )
}
```

> Phase-1 keeps dataset/column creation minimal: datasets + columns are seeded (Task 5) or created via the API; the page edits **rows** (the daily-use case). A full dataset/column create-form is a fast follow — tracked, not blocking. If the reviewer wants create-in-UI now, add a small "新增資料集" form using `createDataset` mirroring RolesTab's create panel.

- [ ] **Step 2: Add the route/nav entry in Root.tsx** (mirror how AI Kitchen / User & Role pages are registered — sidebar item + route)

Open `bpm-admin-ui/src/flowcook/Root.tsx`, find where `UserRolePage`/`AiKitchenPage` are routed and add a sibling entry pointing `'/datasets'` → `<DatasetsPage />`, plus a sidebar nav link labeled "資料集 / Datasets". Use the exact pattern already present for the other pages.

- [ ] **Step 3: Typecheck + manual browser verification**

Run: `cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit` → no errors.

Manual (boot admin-svc :5266 + admin-ui :5174, seeded DB; log in as jack@acme.example):
- Open "資料集 / Datasets" → `tw-regions` listed with `· 8`.
- Select it → grid shows 8 rows, columns 縣市 / 行政區.
- Edit a cell (blur) → persists (reload shows the change).
- "新增列" adds a blank row; fill cells; persists.
- Toggle 停用 on a row → it dims; reload `/dataset-demo` in bpm-ui and confirm the deactivated district no longer appears in the cascading dropdown (but any case that already stored it is unaffected — that's the snapshot guarantee).

- [ ] **Step 4: Commit**

```bash
git add bpm-admin-ui/src/flowcook/pages/DatasetsPage.tsx bpm-admin-ui/src/flowcook/Root.tsx
git commit -m "feat(admin-ui): Datasets editor page (TanStack row grid)"
```

---

## Self-review (completed during planning)

**Spec coverage:**
- Customer-editable datasets without redeploy → Tasks 1-5 (admin CRUD) + 11-12 (editor). ✓
- Design-time binding, editable content (option A) → binding object authored in form/DatasetSelect (Task 9); content edited in admin (Task 12). ✓
- Cascading (filterBy parent value) → Task 7 resolution + Task 9/10 DatasetSelect parentValue. ✓
- Denormalized wide table + distinct + group → Task 7 (`Distinct`, `GroupByColumn`) + demo seed. ✓
- Snapshot value+label → DatasetSelect `onChange(value,label)` (Task 9) + demo captures both (Task 10). (Wiring the snapshot into a real flow's case entity is part of the deferred per-field conversion — Task 10 proves the primitive returns both.) ✓
- Deactivate-not-delete → `IsActive` flag (Task 1), resolution excludes inactive (Task 7), toggle in editor (Task 12). ✓
- DB portability (in-memory filter/distinct/group, TEXT JSON) → Task 7. ✓
- admin-svc owns / bpm-svc Shared read → Tasks 2, 6. ✓
- Seeded cascading demo → Task 5; end-to-end proof → Task 10. ✓
- TanStack Table grid → Tasks 11-12. ✓

**Placeholder scan:** the only deliberate conditionals are (a) the admin-svc unit-test step guarded on "does admin-svc have a test project" (Task 3 pre-step) and (b) the namespace-confirm notes for `IClock`/`IAuditLogger`/`BpmControllerBase` — these are "copy the exact symbol from file X" instructions, not vague TODOs. No "add error handling"/"TBD" placeholders.

**Type consistency:** `ResolveRequest` field names/order are identical across Task 7 (definition), Task 8 (controller + test), and Task 9 (client maps `filterByColumn`→`filterColumn`, `parentValue`→`filterValue` explicitly). `DatasetOption(Value,Label,Group)` consistent. Admin DTO names (`DatasetDto`, `DatasetRowDto`, `CreateDatasetRequest`, etc.) match between Task 3 (server) and Task 11 (client). `DatasetBinding` consistent between Task 9 and Task 10.

**Known follow-ups (explicitly out of phase 1, per spec):** real per-field conversion (post-demo), dataset/column create-in-UI form, Excel/CSV import, AI-Kitchen binding, option-B runtime re-binding, response caching of `/resolve` (add when load warrants — the service is stateless and easy to wrap in a cache later).
