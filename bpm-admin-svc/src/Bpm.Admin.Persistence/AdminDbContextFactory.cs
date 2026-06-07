using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bpm.Admin.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef migrations add` / `database update`.
/// Migrations target SQLite locally; production switches connection string only.
/// </summary>
public class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
{
    public AdminDbContext CreateDbContext(string[] args)
    {
        // Provider for `dotnet ef migrations add` comes from BPM_DB_PROVIDER
        // (postgres | sqlite). Migration scaffolding doesn't open a connection,
        // so a placeholder connection string is fine — only the provider shapes
        // the generated SQL. History table matches the runtime API + SeedCli.
        var provider = DbProviderSetup.ResolveProvider(null);
        var conn = System.Environment.GetEnvironmentVariable("BPM_DB_CONNECTION")
            ?? (DbProviderSetup.IsPostgres(provider)
                ? "Host=localhost;Database=flowcook_designtime;Username=postgres"
                : "Data Source=design-time.db");
        var builder = new DbContextOptionsBuilder<AdminDbContext>();
        DbProviderSetup.Configure(builder, provider, conn);
        return new AdminDbContext(builder.Options);
    }
}
