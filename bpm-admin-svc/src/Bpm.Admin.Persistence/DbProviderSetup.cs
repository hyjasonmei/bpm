using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence;

/// <summary>
/// Single place that decides which EF Core provider admin-svc uses (API host,
/// SeedCli, design-time factory, Seeder). Driven by the <c>Database:Provider</c>
/// config key or the <c>BPM_DB_PROVIDER</c> env var (the non-config callers —
/// the design-time factory + Seeder — only have the env). <c>postgres</c> ⇒
/// Npgsql, anything else ⇒ SQLite.
///
/// admin keeps its migrations in a dedicated history table so admin-svc and
/// bpm-svc can both migrate the same physical database — that table name is
/// preserved across providers here.
/// </summary>
public static class DbProviderSetup
{
    public const string DefaultProvider = "sqlite";
    public const string AdminHistoryTable = "__AdminEFMigrationsHistory";

    public static bool IsPostgres(string? provider) =>
        string.Equals(provider, "postgres", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "postgresql", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "npgsql", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Config value (may be null) → env fallback → default.</summary>
    public static string ResolveProvider(string? configValue) =>
        configValue
        ?? System.Environment.GetEnvironmentVariable("BPM_DB_PROVIDER")
        ?? System.Environment.GetEnvironmentVariable("Database__Provider")
        ?? DefaultProvider;

    public static void Configure(DbContextOptionsBuilder options, string? provider, string connectionString)
    {
        if (IsPostgres(provider))
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(AdminHistoryTable));
        else
            options.UseSqlite(DbPathResolver.Normalize(connectionString),
                sqlite => sqlite.MigrationsHistoryTable(AdminHistoryTable));
    }
}
