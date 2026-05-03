using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Purchase.Dtos;
using Bpm.Application.Purchase.Services;
using Bpm.Domain.Cases;
using Bpm.Domain.States;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Purchase.Commands;

public sealed record ApprovePurchaseCommand(Guid CaseId, string ApproverUserId) : ICommand<PurchaseCaseDto>;

public sealed class ApprovePurchaseCommandValidator : AbstractValidator<ApprovePurchaseCommand>
{
    public ApprovePurchaseCommandValidator()
    {
        RuleFor(x => x.CaseId).NotEqual(Guid.Empty);
        RuleFor(x => x.ApproverUserId).NotEmpty();
    }
}

public sealed class ApprovePurchaseCommandHandler(
    IAppDbContext db,
    IClock clock,
    PurchaseApprovalResolver resolver,
    PurchaseNotificationEmitter emitter
) : IRequestHandler<ApprovePurchaseCommand, PurchaseCaseDto>
{
    public async Task<PurchaseCaseDto> Handle(ApprovePurchaseCommand request, CancellationToken ct)
    {
        var c = await db.PurchaseCases.FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("PurchaseCase", request.CaseId);

        if (c.CurrentApproverUserId != request.ApproverUserId)
            throw new ConflictException(
                $"Approver mismatch: case expects '{c.CurrentApproverUserId}', got '{request.ApproverUserId}'.");

        var now = clock.UtcNow;

        switch (c.State)
        {
            case PurchaseState.PendingManagerApproval:
                string? financeId = null;
                if (c.Amount >= PurchaseCase.FinanceThreshold)
                    financeId = await resolver.ResolveFinanceApproverAsync(ct);
                c.ManagerApprove(request.ApproverUserId, financeId, now);
                break;

            case PurchaseState.PendingFinanceApproval:
                string? ceoId = null;
                if (c.Amount >= PurchaseCase.CeoThreshold)
                    ceoId = await resolver.ResolveCeoApproverAsync(ct);
                c.FinanceApprove(request.ApproverUserId, ceoId, now);
                break;

            case PurchaseState.PendingCeoApproval:
                c.CeoApprove(request.ApproverUserId, now);
                break;

            default:
                throw new ConflictException($"Cannot approve in state {c.State}.");
        }

        await db.SaveChangesAsync(ct);

        var caseUrl = $"/cases/purchase/{c.Id}";
        if (c.State == PurchaseState.PendingFinanceApproval || c.State == PurchaseState.PendingCeoApproval)
            await emitter.EmitOnAssignApproverAsync(c, caseUrl, ct);
        else if (c.State == PurchaseState.PendingPurchaseExec)
            await emitter.EmitOnAssignPurchaseAsync(c, caseUrl, ct);

        return PurchaseCaseDto.FromDomain(c);
    }
}
