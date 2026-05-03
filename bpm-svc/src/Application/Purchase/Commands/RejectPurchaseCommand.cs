using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Purchase.Dtos;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Purchase.Commands;

public sealed record RejectPurchaseCommand(Guid CaseId, string ApproverUserId, string Reason) : ICommand<PurchaseCaseDto>;

public sealed class RejectPurchaseCommandValidator : AbstractValidator<RejectPurchaseCommand>
{
    public RejectPurchaseCommandValidator()
    {
        RuleFor(x => x.CaseId).NotEqual(Guid.Empty);
        RuleFor(x => x.ApproverUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1024);
    }
}

public sealed class RejectPurchaseCommandHandler(IAppDbContext db, IClock clock)
    : IRequestHandler<RejectPurchaseCommand, PurchaseCaseDto>
{
    public async Task<PurchaseCaseDto> Handle(RejectPurchaseCommand request, CancellationToken ct)
    {
        var c = await db.PurchaseCases.FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("PurchaseCase", request.CaseId);

        if (c.CurrentApproverUserId != request.ApproverUserId)
            throw new ConflictException(
                $"Approver mismatch: case expects '{c.CurrentApproverUserId}', got '{request.ApproverUserId}'.");

        c.Reject(request.ApproverUserId, request.Reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return PurchaseCaseDto.FromDomain(c);
    }
}
