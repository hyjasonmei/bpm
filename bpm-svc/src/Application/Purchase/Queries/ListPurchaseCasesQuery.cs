using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Purchase.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Purchase.Queries;

public sealed record ListPurchaseCasesQuery(string? ApplicantUserId, string? CurrentApproverUserId)
    : IQuery<IReadOnlyList<PurchaseCaseDto>>;

public sealed class ListPurchaseCasesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPurchaseCasesQuery, IReadOnlyList<PurchaseCaseDto>>
{
    public async Task<IReadOnlyList<PurchaseCaseDto>> Handle(ListPurchaseCasesQuery request, CancellationToken ct)
    {
        var q = db.PurchaseCases.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(request.ApplicantUserId))
            q = q.Where(x => x.ApplicantUserId == request.ApplicantUserId);
        if (!string.IsNullOrEmpty(request.CurrentApproverUserId))
            q = q.Where(x => x.CurrentApproverUserId == request.CurrentApproverUserId);

        var items = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return items.Select(PurchaseCaseDto.FromDomain).ToList();
    }
}
