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
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new AdminDbContext(options);
    }
}
