using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Audit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bpm.Admin.Api.Tests.TestFixtures;

/// <summary>
/// Boots the API host backed by an in-memory SQLite connection that lives for
/// the lifetime of this factory instance.
/// </summary>
public class AdminAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public AdminAppFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // A1 made the DbContext provider config-driven (default postgres). Force
        // the sqlite branch so Program.cs registers the same provider this factory
        // overrides with — otherwise EF sees both Npgsql + Sqlite registered and
        // throws "Only a single database provider can be registered".
        builder.UseSetting("Database:Provider", "sqlite");
        // Disable startup org-seed: Seeder.SeedOrgAsync opens a NEW connection from
        // the connection string, but in-memory sqlite (":memory:") gives each new
        // connection its own empty db — the seed would write to a schema-less db and
        // fail. These tests build their own fixtures and expect an empty store
        // (e.g. Empty_list_returns_OK), so seeding is unwanted here anyway.
        Environment.SetEnvironmentVariable("FLOWCOOK_ADMIN_SEED_ON_STARTUP", "false");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AdminDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<AdminDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
            });

            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
