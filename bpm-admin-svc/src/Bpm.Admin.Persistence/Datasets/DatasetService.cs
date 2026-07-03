using System.Text.Json;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;   // IClock
using Bpm.Admin.Application.Datasets;
using Bpm.Admin.Domain.Datasets;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Datasets;

public sealed class DatasetService(AdminDbContext db, IClock clock, IAuditLogger audit) : IDatasetService
{
    public async Task<IReadOnlyList<DatasetDto>> ListAsync(CancellationToken ct = default)
    {
        var sets = await db.Datasets.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        var counts = await db.DatasetRows.AsNoTracking()
            .GroupBy(r => r.DatasetId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return sets.Select(d => ToDto(d, counts.GetValueOrDefault(d.Id))).ToList();
    }

    public async Task<DatasetDto> CreateAsync(CreateDatasetRequest req, Guid? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Key)) throw new DatasetException("dataset key required");
        if (await db.Datasets.AnyAsync(d => d.Key == req.Key, ct))
            throw new DatasetException($"dataset key '{req.Key}' already in use");
        var row = new Dataset
        {
            Id = Guid.NewGuid(),
            Key = req.Key.Trim(),
            Name = req.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            ColumnsJson = JsonSerializer.Serialize(req.Columns),
            IsActive = true,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };
        db.Datasets.Add(row);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            actionType: "dataset_created",
            targetType: "dataset",
            targetId: row.Id.ToString(),
            actorUserId: actor,
            actorPrincipalId: null,
            after: new { row.Key, row.Name },
            ct: ct);

        return ToDto(row, 0);
    }

    public async Task<DatasetDto> UpdateAsync(Guid id, UpdateDatasetRequest req, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DatasetException("dataset not found");
        if (req.Name is not null) row.Name = req.Name.Trim();
        if (req.Description is not null) row.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description;
        if (req.Columns is not null) row.ColumnsJson = JsonSerializer.Serialize(req.Columns);
        if (req.IsActive is not null) row.IsActive = req.IsActive.Value;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            actionType: "dataset_updated",
            targetType: "dataset",
            targetId: id.ToString(),
            actorUserId: actor,
            actorPrincipalId: null,
            after: new { row.Name, row.IsActive },
            ct: ct);

        var n = await db.DatasetRows.CountAsync(r => r.DatasetId == id, ct);
        return ToDto(row, n);
    }

    public async Task DeleteAsync(Guid id, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new DatasetException("dataset not found");
        row.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            actionType: "dataset_deleted",
            targetType: "dataset",
            targetId: id.ToString(),
            actorUserId: actor,
            actorPrincipalId: null,
            ct: ct);
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
        var row = new DatasetRow
        {
            Id = Guid.NewGuid(),
            DatasetId = datasetId,
            CellsJson = JsonSerializer.Serialize(req.Cells),
            IsActive = true,
            SortOrder = maxOrder + 1,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };
        db.DatasetRows.Add(row);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            actionType: "dataset_row_added",
            targetType: "dataset_row",
            targetId: row.Id.ToString(),
            actorUserId: actor,
            actorPrincipalId: null,
            after: new { datasetId },
            ct: ct);

        return ToRowDto(row);
    }

    public async Task<DatasetRowDto> UpdateRowAsync(Guid rowId, UpdateRowRequest req, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.DatasetRows.FirstOrDefaultAsync(r => r.Id == rowId, ct)
            ?? throw new DatasetException("row not found");
        if (req.Cells is not null) row.CellsJson = JsonSerializer.Serialize(req.Cells);
        if (req.IsActive is not null) row.IsActive = req.IsActive.Value;
        if (req.SortOrder is not null) row.SortOrder = req.SortOrder.Value;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            actionType: "dataset_row_updated",
            targetType: "dataset_row",
            targetId: rowId.ToString(),
            actorUserId: actor,
            actorPrincipalId: null,
            after: new { row.IsActive },
            ct: ct);

        return ToRowDto(row);
    }

    public async Task DeleteRowAsync(Guid rowId, Guid? actor, CancellationToken ct = default)
    {
        var row = await db.DatasetRows.FirstOrDefaultAsync(r => r.Id == rowId, ct)
            ?? throw new DatasetException("row not found");
        row.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(
            actionType: "dataset_row_deleted",
            targetType: "dataset_row",
            targetId: rowId.ToString(),
            actorUserId: actor,
            actorPrincipalId: null,
            ct: ct);
    }

    private static DatasetDto ToDto(Dataset d, int rowCount) => new(
        d.Id, d.Key, d.Name, d.Description,
        JsonSerializer.Deserialize<List<DatasetColumnDef>>(d.ColumnsJson) ?? new(), d.IsActive, rowCount);

    private static DatasetRowDto ToRowDto(DatasetRow r) => new(
        r.Id, r.DatasetId,
        JsonSerializer.Deserialize<Dictionary<string, string>>(r.CellsJson) ?? new(), r.IsActive, r.SortOrder);
}
