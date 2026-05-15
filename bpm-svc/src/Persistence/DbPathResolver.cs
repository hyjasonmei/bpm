using System.IO;

namespace Bpm.Persistence;

/// <summary>
/// Normalizes SQLite "Data Source=..." paths so the Api server and SeedCli
/// always land on the same DB file regardless of CWD. Relative paths resolve
/// to &lt;repoRoot&gt;/db/&lt;filename&gt;; absolute paths pass through unchanged.
/// </summary>
public static class DbPathResolver
{
    public static string Normalize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;

        var parts = connectionString.Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) continue;
            var key = parts[i][..eq].Trim();
            var value = parts[i][(eq + 1)..].Trim();
            if (!string.Equals(key, "Data Source", System.StringComparison.OrdinalIgnoreCase)) continue;

            if (string.IsNullOrWhiteSpace(value) || value == ":memory:") return connectionString;
            if (Path.IsPathRooted(value)) return connectionString;

            var repoRoot = FindRepoRoot();
            if (repoRoot is null) return connectionString;

            var dbDir = Path.Combine(repoRoot, "db");
            Directory.CreateDirectory(dbDir);
            var abs = Path.Combine(dbDir, Path.GetFileName(value));
            parts[i] = $"{key}={abs}";
            return string.Join(';', parts);
        }
        return connectionString;
    }

    private static string? FindRepoRoot()
    {
        var probe = System.AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(probe, ".git"))) return probe;
            var parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent) || parent == probe) break;
            probe = parent;
        }
        return null;
    }
}
