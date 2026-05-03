using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Purchase.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Purchase.Queries;

public sealed record GetPurchaseCaseQuery(Guid CaseId) : IQuery<PurchaseCaseDto>;

public sealed class GetPurchaseCaseQueryHandler(IAppDbContext db)
    : IRequestHandler<GetPurchaseCaseQuery, PurchaseCaseDto>
{
    public async Task<PurchaseCaseDto> Handle(GetPurchaseCaseQuery request, CancellationToken ct)
    {
        var c = await db.PurchaseCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("PurchaseCase", request.CaseId);
        return PurchaseCaseDto.FromDomain(c);
    }
}
