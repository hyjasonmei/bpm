using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence;

/// <summary>
/// Single place that decides which EF Core provider the runtime uses, so the
/// Api host, SeedCli, and any design-time tooling stay in lockstep. Driven by
/// the <c>Database:Provider</c> config key (env: <c>Database__Provider</c>) —
/// <c>postgres</c> ⇒ Npgsql, anything else ⇒ SQLite (the default keeps the
/// in-memory test path + legacy local file db working).
///
/// SQLite connection strings are run through <see cref="DbPathResolver"/> so
/// relative "Data Source=" paths land under &lt;repoRoot&gt;/db; Postgres
/// connection strings (Host=…;Database=…) pass through untouched.
/// </summary>
public static class DbProviderSetup
{
    public const string DefaultProvider = "sqlite";

    public static bool IsPostgres(string? provider) =>
        string.Equals(provider, "postgres", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "postgresql", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "npgsql", System.StringComparison.OrdinalIgnoreCase);

    public static void Configure(DbContextOptionsBuilder options, string? provider, string connectionString)
    {
        if (IsPostgres(provider))
            options.UseNpgsql(connectionString);
        else
            options.UseSqlite(DbPathResolver.Normalize(connectionString));
    }
}
