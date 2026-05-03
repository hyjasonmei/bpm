using System.Reflection;
using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork, IAppDbContext
{
    public DbSet<TravelCase> TravelCases => Set<TravelCase>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(builder);
    }
}
