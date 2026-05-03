using Bpm.Domain.Cases;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Common.Abstractions;

public interface IAppDbContext
{
    DbSet<PurchaseCase> PurchaseCases { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
