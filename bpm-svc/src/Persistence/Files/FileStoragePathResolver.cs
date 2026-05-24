using System.IO;

namespace Bpm.Persistence.Files;

/// <summary>
/// Resolves the on-disk root where file-blob bytes live. Mirrors
/// <see cref="DbPathResolver"/>: relative paths land under
/// <c>&lt;repoRoot&gt;/db/files/</c> so the dev server and any CLI tooling
/// converge on the same directory regardless of CWD.
/// </summary>
public static class FileStoragePathResolver
{
    public const string DefaultRelativePath = "files";

    /// <summary>
    /// Given the configured path (which may be null / empty / relative / absolute),
    /// returns an absolute directory. Creates it if missing so callers don't have
    /// to repeat the dance.
    /// </summary>
    public static string Resolve(string? configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? DefaultRelativePath : configuredPath.Trim();

        if (!Path.IsPathRooted(path))
        {
            var repoRoot = FindRepoRoot();
            if (repoRoot is not null)
            {
                // Mirror DbPathResolver: relative file-storage paths land under
                // <repoRoot>/db/<relative>/ so SQLite db + blob files sit side-by-side.
                path = Path.Combine(repoRoot, "db", path);
            }
            else
            {
                path = Path.GetFullPath(path);
            }
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static string? FindRepoRoot()
    {
        var probe = System.AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var gitPath = Path.Combine(probe, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath)) return probe;
            var parent = Path.GetDirectoryName(probe);
            if (string.IsNullOrEmpty(parent) || parent == probe) break;
            probe = parent;
        }
        return null;
    }
}
