using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Travel.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Travel.Queries;

public sealed record GetTravelCaseQuery(Guid CaseId) : IQuery<TravelCaseDto>;

public sealed class GetTravelCaseQueryHandler(IAppDbContext db)
    : IRequestHandler<GetTravelCaseQuery, TravelCaseDto>
{
    public async Task<TravelCaseDto> Handle(GetTravelCaseQuery request, CancellationToken ct)
    {
        var c = await db.TravelCases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("TravelCase", request.CaseId);
        return TravelCaseDto.FromDomain(c);
    }
}
