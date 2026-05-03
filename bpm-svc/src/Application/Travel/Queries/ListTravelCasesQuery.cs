using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Travel.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Travel.Queries;

public sealed record ListTravelCasesQuery(string? ApplicantUserId, string? CurrentApproverUserId)
    : IQuery<IReadOnlyList<TravelCaseDto>>;

public sealed class ListTravelCasesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListTravelCasesQuery, IReadOnlyList<TravelCaseDto>>
{
    public async Task<IReadOnlyList<TravelCaseDto>> Handle(ListTravelCasesQuery request, CancellationToken ct)
    {
        var q = db.TravelCases.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(request.ApplicantUserId))
            q = q.Where(x => x.ApplicantUserId == request.ApplicantUserId);
        if (!string.IsNullOrEmpty(request.CurrentApproverUserId))
            q = q.Where(x => x.CurrentApproverUserId == request.CurrentApproverUserId);

        var items = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return items.Select(TravelCaseDto.FromDomain).ToList();
    }
}
