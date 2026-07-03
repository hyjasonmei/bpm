using System.Text.Json;
using Bpm.Application.Datasets;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Datasets;

/// Resolves a dataset binding into dropdown options. Loads the dataset's active
/// rows, then applies filter → project → sort → distinct → group IN MEMORY
/// (DB-portable: no JSON-path SQL). Reference-data sizes make this cheap.
/// Impl lives in Persistence (needs AppDbContext); interface is in Application.
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
            projected = projected.GroupBy(o => (o.Value, o.Label, o.Group)).Select(g => g.First());

        return projected.ToList();
    }
}
