using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Identity;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Purchase.Dtos;
using Bpm.Application.Purchase.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Application.Purchase.Commands;

public sealed record ExecutePurchaseCommand(
    Guid CaseId,
    string ExecUserId,
    string PoNumber,
    DateOnly ExpectedDelivery,
    string? ExecNote
) : ICommand<PurchaseCaseDto>;

public sealed class ExecutePurchaseCommandValidator : AbstractValidator<ExecutePurchaseCommand>
{
    public ExecutePurchaseCommandValidator()
    {
        RuleFor(x => x.CaseId).NotEqual(Guid.Empty);
        RuleFor(x => x.ExecUserId).NotEmpty();
        RuleFor(x => x.PoNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ExpectedDelivery).NotEqual(default(DateOnly));
    }
}

public sealed class ExecutePurchaseCommandHandler(
    IAppDbContext db,
    IClock clock,
    IIdentityProvider identity,
    PurchaseNotificationEmitter emitter
) : IRequestHandler<ExecutePurchaseCommand, PurchaseCaseDto>
{
    public async Task<PurchaseCaseDto> Handle(ExecutePurchaseCommand request, CancellationToken ct)
    {
        var c = await db.PurchaseCases.FirstOrDefaultAsync(x => x.Id == request.CaseId, ct)
            ?? throw new NotFoundException("PurchaseCase", request.CaseId);

        var execUser = await identity.FindByIdAsync(request.ExecUserId, ct)
            ?? throw new NotFoundException("Employee", request.ExecUserId);
        if (!execUser.Roles.Contains("Purchase"))
            throw new ConflictException(
                $"User '{request.ExecUserId}' is not in role 'Purchase' (spec.userTasks[task_purchase_exec].permissions.submitter).");

        c.Execute(request.ExecUserId, request.PoNumber, request.ExpectedDelivery, request.ExecNote, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await emitter.EmitOnCompleteAsync(c, ct);

        return PurchaseCaseDto.FromDomain(c);
    }
}
