using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Purchase.Dtos;
using Bpm.Application.Purchase.Services;
using Bpm.Domain.Cases;
using FluentValidation;
using MediatR;

namespace Bpm.Application.Purchase.Commands;

public sealed record SubmitPurchaseCommand(
    string TenantCode,
    string ApplicantUserId,
    string Vendor,
    string Category,
    decimal Amount,
    string Items,
    string Justification,
    string? QuoteFileName
) : ICommand<PurchaseCaseDto>;

public sealed class SubmitPurchaseCommandValidator : AbstractValidator<SubmitPurchaseCommand>
{
    private static readonly string[] AllowedCategories = { "office", "it", "service", "other" };

    public SubmitPurchaseCommandValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty();
        RuleFor(x => x.ApplicantUserId).NotEmpty();
        RuleFor(x => x.Vendor).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)} (spec.userTasks[task_request].fields[category].options).");
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .LessThanOrEqualTo(10_000_000)
            .WithMessage("Amount must satisfy 0 < value <= 10,000,000 (spec.userTasks[task_request].fields[amount].validator).");
        RuleFor(x => x.Items).NotEmpty().MaximumLength(4096);
        RuleFor(x => x.Justification).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.QuoteFileName)
            .NotEmpty()
            .When(x => x.Amount >= PurchaseCase.FinanceThreshold)
            .WithMessage("quote_file is required when amount >= 10000");
    }
}

public sealed class SubmitPurchaseCommandHandler(
    IAppDbContext db,
    IClock clock,
    PurchaseApprovalResolver resolver,
    PurchaseNotificationEmitter emitter
) : IRequestHandler<SubmitPurchaseCommand, PurchaseCaseDto>
{
    public async Task<PurchaseCaseDto> Handle(SubmitPurchaseCommand request, CancellationToken ct)
    {
        var managerId = await resolver.ResolveManagerApproverAsync(request.ApplicantUserId, ct);

        var c = PurchaseCase.Submit(
            tenantCode: request.TenantCode,
            applicantUserId: request.ApplicantUserId,
            vendor: request.Vendor,
            category: request.Category,
            amount: request.Amount,
            items: request.Items,
            justification: request.Justification,
            quoteFileName: request.QuoteFileName,
            firstApproverUserId: managerId,
            now: clock.UtcNow);

        db.PurchaseCases.Add(c);
        await db.SaveChangesAsync(ct);

        await emitter.EmitOnAssignApproverAsync(c, caseUrl: $"/cases/purchase/{c.Id}", ct);

        return PurchaseCaseDto.FromDomain(c);
    }
}
