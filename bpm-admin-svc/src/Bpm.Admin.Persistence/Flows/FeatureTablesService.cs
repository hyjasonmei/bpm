using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public sealed class FeatureTablesService : IFeatureTablesService
{
    private readonly AdminDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    // <CODE>_V<N>_<rest>; CODE = upper alphanumeric or underscore-segments,
    // N = digits. Archived tables (ending __arch_<hex>) are matched
    // separately by ArchiveSuffix and excluded from the live capture.
    private static readonly Regex FeaturePattern = new(
        @"^(?<code>[A-Z][A-Z0-9_]*?)_V(?<version>\d+)_(?<rest>.+?)(?<arch>__arch_[0-9a-f]+)?$",
        RegexOptions.Compiled);

    public FeatureTablesService(AdminDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FeatureTableGroupDto>> ScanAsync(CancellationToken ct = default)
    {
        var allTables = await ReadTableNamesAsync(ct);
        // Group physical tables by <CODE>_V<N> + Archived bool.
        var live = new Dictionary<(string Code, int Version), List<string>>();
        var archived = new Dictionary<(string Code, int Version), List<string>>();
        foreach (var t in allTables)
        {
            if (t.StartsWith("Admin_", StringComparison.Ordinal)) continue;
            if (t.StartsWith("__", StringComparison.Ordinal)) continue;
            if (t.StartsWith("sqlite_", StringComparison.Ordinal)) continue;
            var m = FeaturePattern.Match(t);
            if (!m.Success) continue;
            var key = (m.Groups["code"].Value, int.Parse(m.Groups["version"].Value, CultureInfo.InvariantCulture));
            var target = m.Groups["arch"].Success ? archived : live;
            if (!target.TryGetValue(key, out var bucket))
            {
                bucket = new List<string>();
                target[key] = bucket;
            }
            bucket.Add(t);
        }

        // Pull every Admin_Flow row (even archived) so we can classify
        // each <CODE>_V<N> bundle as Linked / Orphan / Archived /
        // Dangling. Soft-deleted rows skipped (global filter).
        var flows = await _db.Flows
            .AsNoTracking()
            .ToListAsync(ct);
        var flowIndex = flows.ToLookup(f => (Code: f.FlowCode, Version: f.Version));

        var seen = new HashSet<(string, int)>();
        var output = new List<FeatureTableGroupDto>();

        foreach (var (key, tables) in live.OrderBy(kv => kv.Key.Code).ThenBy(kv => kv.Key.Version))
        {
            seen.Add(key);
            tables.Sort(StringComparer.Ordinal);
            var matchingFlows = flowIndex[key].OrderByDescending(f => f.UpdatedAt).ToList();
            if (matchingFlows.Count == 0)
            {
                output.Add(new FeatureTableGroupDto(
                    FlowCode: key.Code, Version: key.Version,
                    Status: "Orphan",
                    FlowId: null, FlowDisplayName: null, FlowState: null,
                    ArchivedAt: null,
                    TableNames: tables,
                    ArchivedTableNames: Array.Empty<string>()));
            }
            else
            {
                var primary = matchingFlows[0];
                output.Add(new FeatureTableGroupDto(
                    FlowCode: key.Code, Version: key.Version,
                    Status: "Linked",
                    FlowId: primary.Id, FlowDisplayName: primary.DisplayName, FlowState: primary.State.ToString(),
                    ArchivedAt: null,
                    TableNames: tables,
                    ArchivedTableNames: Array.Empty<string>()));
            }
        }
        foreach (var (key, tables) in archived.OrderBy(kv => kv.Key.Code).ThenBy(kv => kv.Key.Version))
        {
            seen.Add(key);
            tables.Sort(StringComparer.Ordinal);
            var matchingFlow = flowIndex[key].FirstOrDefault(f => f.ArchivedAt != null);
            output.Add(new FeatureTableGroupDto(
                FlowCode: key.Code, Version: key.Version,
                Status: "Archived",
                FlowId: matchingFlow?.Id,
                FlowDisplayName: matchingFlow?.DisplayName,
                FlowState: matchingFlow?.State.ToString(),
                ArchivedAt: matchingFlow?.ArchivedAt,
                TableNames: Array.Empty<string>(),
                ArchivedTableNames: tables));
        }
        // Dangling: Admin_Flow row exists but no tables either live or
        // archived (chef hasn't cooked yet).
        foreach (var f in flows)
        {
            var key = (f.FlowCode, f.Version);
            if (seen.Contains(key)) continue;
            output.Add(new FeatureTableGroupDto(
                FlowCode: f.FlowCode, Version: f.Version,
                Status: "Dangling",
                FlowId: f.Id, FlowDisplayName: f.DisplayName, FlowState: f.State.ToString(),
                ArchivedAt: f.ArchivedAt,
                TableNames: Array.Empty<string>(),
                ArchivedTableNames: Array.Empty<string>()));
        }

        return output;
    }

    public async Task<FeatureTableGroupDto> ArchiveAsync(ArchiveFeatureRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.FlowCode))
            throw new FlowLifecycleException("flowCode required");

        // Re-scan and find the live tables to archive.
        var liveTables = (await ReadTableNamesAsync(ct))
            .Where(t => StartsWithPrefix(t, req.FlowCode, req.Version) && !ContainsArchiveSuffix(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
        if (liveTables.Count == 0)
            throw new FlowLifecycleException($"No live tables matching {req.FlowCode}_V{req.Version}_*");

        // Hash anchor: Flow id when available (stable), else random.
        var flow = req.FlowId.HasValue
            ? await _db.Flows.FirstOrDefaultAsync(f => f.Id == req.FlowId.Value, ct)
            : null;
        var hash = (flow?.Id ?? Guid.NewGuid()).ToString("N").Substring(0, 8);

        var renamed = new List<string>();
        var conn = _db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            foreach (var t in liveTables)
            {
                var target = $"{t}__arch_{hash}";
                await ExecAsync(conn, $"ALTER TABLE \"{Quote(t)}\" RENAME TO \"{Quote(target)}\";", ct);
                renamed.Add(target);
            }
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }

        if (flow != null)
        {
            flow.ArchivedAt = _clock.UtcNow;
            flow.UpdatedAt = _clock.UtcNow;
            flow.ArchivedTableNamesJson = JsonSerializer.Serialize(renamed);
            await _db.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(
            actionType: "feature_archived",
            targetType: "feature_table_group",
            targetId: $"{req.FlowCode}_V{req.Version}",
            actorUserId: actorUserId,
            actorPrincipalId: null,
            after: new { req.FlowCode, req.Version, RenamedTo = renamed },
            ct: ct);

        return new FeatureTableGroupDto(
            FlowCode: req.FlowCode, Version: req.Version,
            Status: "Archived",
            FlowId: flow?.Id, FlowDisplayName: flow?.DisplayName, FlowState: flow?.State.ToString(),
            ArchivedAt: flow?.ArchivedAt,
            TableNames: Array.Empty<string>(),
            ArchivedTableNames: renamed);
    }

    public async Task<FeatureTableGroupDto> RestoreAsync(RestoreFeatureRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.FlowCode))
            throw new FlowLifecycleException("flowCode required");

        var all = await ReadTableNamesAsync(ct);
        var archivedTables = all
            .Where(t => StartsWithPrefix(t, req.FlowCode, req.Version) && ContainsArchiveSuffix(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
        if (archivedTables.Count == 0)
            throw new FlowLifecycleException($"No archived tables matching {req.FlowCode}_V{req.Version}_*__arch_*");

        // Conflict guard: original namespace must be free.
        foreach (var arch in archivedTables)
        {
            var originalName = arch.Substring(0, arch.LastIndexOf("__arch_", StringComparison.Ordinal));
            if (all.Contains(originalName))
                throw new FlowLifecycleException(
                    $"Cannot restore: table '{originalName}' already exists — likely a newer cook took the namespace.");
        }

        var restored = new List<string>();
        var conn = _db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            foreach (var arch in archivedTables)
            {
                var originalName = arch.Substring(0, arch.LastIndexOf("__arch_", StringComparison.Ordinal));
                await ExecAsync(conn, $"ALTER TABLE \"{Quote(arch)}\" RENAME TO \"{Quote(originalName)}\";", ct);
                restored.Add(originalName);
            }
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }

        var flow = await _db.Flows
            .Where(f => f.FlowCode == req.FlowCode && f.Version == req.Version && f.ArchivedAt != null)
            .OrderByDescending(f => f.ArchivedAt)
            .FirstOrDefaultAsync(ct);
        if (flow != null)
        {
            flow.ArchivedAt = null;
            flow.UpdatedAt = _clock.UtcNow;
            flow.ArchivedTableNamesJson = null;
            await _db.SaveChangesAsync(ct);
        }

        await _audit.LogAsync(
            actionType: "feature_restored",
            targetType: "feature_table_group",
            targetId: $"{req.FlowCode}_V{req.Version}",
            actorUserId: actorUserId,
            actorPrincipalId: null,
            after: new { req.FlowCode, req.Version, RestoredTo = restored },
            ct: ct);

        return new FeatureTableGroupDto(
            FlowCode: req.FlowCode, Version: req.Version,
            Status: flow == null ? "Orphan" : "Linked",
            FlowId: flow?.Id, FlowDisplayName: flow?.DisplayName, FlowState: flow?.State.ToString(),
            ArchivedAt: null,
            TableNames: restored,
            ArchivedTableNames: Array.Empty<string>());
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static bool StartsWithPrefix(string table, string code, int version)
    {
        var prefix = $"{code}_V{version}_";
        return table.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool ContainsArchiveSuffix(string table)
    {
        var idx = table.LastIndexOf("__arch_", StringComparison.Ordinal);
        return idx > 0;
    }

    private async Task<IReadOnlyList<string>> ReadTableNamesAsync(CancellationToken ct)
    {
        var names = new List<string>();
        var conn = _db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
        return names;
    }

    private static async Task ExecAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Escape any double-quote in the identifier per SQLite rules.</summary>
    private static string Quote(string raw) => raw.Replace("\"", "\"\"");
}
