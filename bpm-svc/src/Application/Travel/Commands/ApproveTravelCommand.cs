using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Travel.Dtos;
using Bpm.Application.Travel.Services;
using Bpm.Domain.States;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Travel.Commands;

public sealed record ApproveTravelCommand(Guid CaseId, string ApproverUserId) : ICommand<TravelCaseDto>;

public sealed class ApproveTravelCommandValidator : AbstractValidator<ApproveTravelCommand>
{
    public ApproveTravelCommandValidator()
    {
        RuleFor(x => x.CaseId).NotEqual(Guid.Empty);
        RuleFor(x => x.ApproverUserId).NotEmpty();
    }
}

public sealed class ApproveTravelCommandHandler(
    IAppDbContext db,
    IClock clock,
    TravelApprovalResolver resolver,
    TravelNotificationEmitter emitter
) : IRequestHandler<ApproveTravelCommand, TravelCaseDto>
{
    public async Task<TravelCaseDto> Handle(ApproveTravelCommand request, CancellationToken ct)
    {
        var c = await db.TravelCases.FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("TravelCase", request.CaseId);

        if (c.CurrentApproverUserId != request.ApproverUserId)
            throw new ConflictException(
                $"Approver mismatch: case expects '{c.CurrentApproverUserId}', got '{request.ApproverUserId}'.");

        var now = clock.UtcNow;

        switch (c.State)
        {
            case TravelState.PendingManagerApproval:
                string? vpId = null;
                if (c.DestinationType == "international")
                    vpId = await resolver.ResolveVpApproverAsync(c.ApplicantUserId, ct);
                c.ManagerApprove(request.ApproverUserId, vpId, now);
                break;

            case TravelState.PendingVpApproval:
                c.VpApprove(request.ApproverUserId, now);
                break;

            default:
                throw new ConflictException($"Cannot approve in state {c.State}.");
        }

        await db.SaveChangesAsync(ct);

        var caseUrl = $"/cases/travel/{c.Id}";
        if (c.State == TravelState.PendingVpApproval)
            await emitter.EmitOnAssignApproverAsync(c, caseUrl, ct);
        else if (c.State == TravelState.PendingAdminBook)
            await emitter.EmitOnAssignAdminAsync(c, caseUrl, ct);

        return TravelCaseDto.FromDomain(c);
    }
}
