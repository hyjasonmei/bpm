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
