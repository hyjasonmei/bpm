using System.Text;
using System.Text.Json;
using System.Xml;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using Microsoft.OData.UriParser;

namespace Bpm.Admin.Api.Odata;

/// <summary>
/// 1.1b — custom datasets exposed as dynamic OData tables under /odata-ds. Each
/// dataset is its own entity set (columns from ColumnsJson); Power BI / Excel /
/// Power Automate pull one table per dataset and can $filter a single column.
/// Read-only (writes go through the admin UI). Same OdataBasic integration
/// credential as /odata. A plain controller (not OData routing) so auth, dynamic
/// routing, and $metadata all behave — we emit CSDL + OData JSON ourselves and
/// apply query options in-memory (see DatasetFilterEvaluator).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
[Route("odata-ds")]
public sealed class DatasetsODataController(AdminDbContext db) : ControllerBase
{
    private sealed record ColDef(string Key);

    private async Task<List<(Guid Id, string SetName, string OriginalKey, List<string> Cols)>> LoadDefsAsync(CancellationToken ct)
    {
        var datasets = await db.Datasets.AsNoTracking().Where(d => d.DeletedAt == null).ToListAsync(ct);
        var defs = new List<(Guid, string, string, List<string>)>();
        foreach (var d in datasets)
        {
            var cols = (JsonSerializer.Deserialize<List<ColDef>>(d.ColumnsJson) ?? new())
                .Select(c => c.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            defs.Add((d.Id, DatasetEdmModel.SafeName(d.Key), d.Key, cols));
        }
        return defs;
    }

    private IEdmModel BuildModel(IEnumerable<(Guid Id, string SetName, string OriginalKey, List<string> Cols)> defs)
        => DatasetEdmModel.Build(defs.Select(d => new DatasetDef(d.SetName, d.SetName, d.Cols)).ToList());

    // GET /odata-ds/$metadata  → CSDL describing every dataset table (for BI discovery)
    [HttpGet("$metadata")]
    public async Task<IActionResult> Metadata(CancellationToken ct)
    {
        var model = BuildModel(await LoadDefsAsync(ct));
        var sb = new StringBuilder();
        using (var xw = XmlWriter.Create(sb, new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true, Async = false }))
        {
            if (!CsdlWriter.TryWriteCsdl(model, xw, CsdlTarget.OData, out IEnumerable<EdmError> errors))
                return Problem("Failed to write $metadata: " + string.Join("; ", errors.Select(e => e.ErrorMessage)));
            xw.Flush();
        }
        return Content(sb.ToString(), "application/xml");
    }

    // GET /odata-ds  → service document (list of dataset tables)
    [HttpGet("")]
    public async Task<IActionResult> ServiceDoc(CancellationToken ct)
    {
        var defs = await LoadDefsAsync(ct);
        return new JsonResult(new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{Root()}/$metadata",
            ["value"] = defs.Select(d => new { name = d.SetName, kind = "EntitySet", url = d.SetName }).ToList(),
        });
    }

    // GET /odata-ds/{key}  → the dataset's rows, with $filter/$select/$orderby/$top/$count
    [HttpGet("{key}")]
    public async Task<IActionResult> Rows(string key, CancellationToken ct)
    {
        var defs = await LoadDefsAsync(ct);
        var def = defs.FirstOrDefault(d => d.SetName == key);
        if (def.SetName is null) return NotFound($"Unknown dataset '{key}'.");

        var model = BuildModel(defs);
        var entitySet = model.EntityContainer.FindEntitySet(def.SetName);
        var entityType = (IEdmEntityType)entitySet!.EntityType();

        var rawRows = await db.DatasetRows.AsNoTracking()
            .Where(r => r.DatasetId == def.Id && r.IsActive && r.DeletedAt == null)
            .OrderBy(r => r.SortOrder).ToListAsync(ct);

        // materialise rows as column→value dictionaries (+ Id)
        var rows = rawRows.Select(r =>
        {
            var cells = JsonSerializer.Deserialize<Dictionary<string, string?>>(r.CellsJson) ?? new();
            var row = new Dictionary<string, string?>(StringComparer.Ordinal) { ["Id"] = r.Id.ToString() };
            foreach (var col in def.Cols) row[col] = cells.TryGetValue(col, out var v) ? v : null;
            return row;
        }).ToList();

        var q = Request.Query;

        // $filter — parse against the EDM, evaluate in-memory
        if (q.TryGetValue("$filter", out var filterStr) && !string.IsNullOrWhiteSpace(filterStr))
        {
            try
            {
                var parser = new ODataQueryOptionParser(model, entityType, entitySet,
                    new Dictionary<string, string> { ["$filter"] = filterStr! });
                var filter = parser.ParseFilter();
                rows = rows.Where(row => DatasetFilterEvaluator.Matches(filter, row)).ToList();
            }
            catch (ODataException ex) { return BadRequest("Invalid $filter: " + ex.Message); }
        }

        // $orderby col [asc|desc]
        if (q.TryGetValue("$orderby", out var orderBy) && !string.IsNullOrWhiteSpace(orderBy))
        {
            var parts = orderBy.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var col = parts[0];
            var desc = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            rows = (desc ? rows.OrderByDescending(r => r.GetValueOrDefault(col), StringComparer.Ordinal)
                         : rows.OrderBy(r => r.GetValueOrDefault(col), StringComparer.Ordinal)).ToList();
        }

        var totalForCount = rows.Count;

        // $top
        if (q.TryGetValue("$top", out var topStr) && int.TryParse(topStr, out var top) && top >= 0)
            rows = rows.Take(top).ToList();

        // $select — project a subset of columns (Id always kept)
        var selectCols = def.Cols;
        if (q.TryGetValue("$select", out var selectStr) && !string.IsNullOrWhiteSpace(selectStr))
        {
            var wanted = selectStr.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            selectCols = def.Cols.Where(c => wanted.Contains(c, StringComparer.Ordinal)).ToList();
        }

        var value = rows.Select(r =>
        {
            var o = new Dictionary<string, object?> { ["Id"] = r["Id"] };
            foreach (var col in selectCols) o[col] = r.GetValueOrDefault(col);
            return o;
        }).ToList();

        var body = new Dictionary<string, object?> { ["@odata.context"] = $"{Root()}/$metadata#{def.SetName}" };
        if (q.TryGetValue("$count", out var cnt) && cnt.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
            body["@odata.count"] = totalForCount;
        body["value"] = value;
        return new JsonResult(body);
    }

    private string Root() => $"{Request.Scheme}://{Request.Host}/odata-ds";
}
