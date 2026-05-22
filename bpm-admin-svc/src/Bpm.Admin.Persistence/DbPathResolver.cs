using System.IO;

namespace Bpm.Admin.Persistence;

/// <summary>
/// Normalizes SQLite "Data Source=..." paths so the Admin API, the
/// bpm API, and any SeedCli all land on the same shared db file under
/// the repo root regardless of CWD. Relative paths resolve to
/// &lt;repoRoot&gt;/db/&lt;filename&gt;; absolute paths pass through unchanged.
///
/// This is a copy of Bpm.Persistence.DbPathResolver (bpm-svc side);
/// both services need the same behaviour but live in separate
/// solutions so direct sharing isn't possible without introducing a
/// shared package. Keep the two files in sync.
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
            var gitPath = Path.Combine(probe, ".git");
            // Main checkout: `.git` is a directory.
            if (Directory.Exists(gitPath)) return probe;
            // Worktree: `.git` is a file containing `gitdir: <abs path to
            // main repo's .git/worktrees/<name>>`. The worktree IS its
            // own root from chef's perspective — db / artefacts should
            // live alongside it, not in the main checkout.
            if (File.Exists(gitPath)) return probe;
            var parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent) || parent == probe) break;
            probe = parent;
        }
        return null;
    }
}
