namespace Bpm.ChefAgent;

/// <summary>
/// Cross-platform global single-instance guard. A named Mutex would throw
/// PlatformNotSupportedException on macOS/Linux, so we hold an exclusive
/// FileStream instead: the second concurrent run fails to open it and exits
/// cleanly. A cook can run 30+ minutes; overlapping 5-minute wake-ups must
/// not pile up.
/// </summary>
public static class SingleInstanceLock
{
    /// <summary>Returns the held stream on success, or null when another
    /// instance already holds the lock (caller should exit 0).</summary>
    public static FileStream? TryAcquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
