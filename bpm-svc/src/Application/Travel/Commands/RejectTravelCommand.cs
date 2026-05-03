using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Travel.Dtos;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Travel.Commands;

public sealed record RejectTravelCommand(Guid CaseId, string ApproverUserId, string Reason) : ICommand<TravelCaseDto>;

public sealed class RejectTravelCommandValidator : AbstractValidator<RejectTravelCommand>
{
    public RejectTravelCommandValidator()
    {
        RuleFor(x => x.CaseId).NotEqual(Guid.Empty);
        RuleFor(x => x.ApproverUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1024);
    }
}

public sealed class RejectTravelCommandHandler(IAppDbContext db, IClock clock)
    : IRequestHandler<RejectTravelCommand, TravelCaseDto>
{
    public async Task<TravelCaseDto> Handle(RejectTravelCommand request, CancellationToken ct)
    {
        var c = await db.TravelCases.FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("TravelCase", request.CaseId);

        if (c.CurrentApproverUserId != request.ApproverUserId)
            throw new ConflictException(
                $"Approver mismatch: case expects '{c.CurrentApproverUserId}', got '{request.ApproverUserId}'.");

        c.Reject(request.ApproverUserId, request.Reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return TravelCaseDto.FromDomain(c);
    }
}
