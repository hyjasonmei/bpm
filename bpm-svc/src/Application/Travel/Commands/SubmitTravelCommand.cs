using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Messaging;
using Bpm.Application.Travel.Dtos;
using Bpm.Application.Travel.Services;
using Bpm.Domain.Cases;
using FluentValidation;
using MediatR;

namespace Bpm.Application.Travel.Commands;

public sealed record SubmitTravelCommand(
    string TenantCode,
    string ApplicantUserId,
    string DestinationType,
    string Destination,
    DateOnly DepartDate,
    DateOnly ReturnDate,
    string Purpose,
    decimal EstimatedCost
) : ICommand<TravelCaseDto>;

public sealed class SubmitTravelCommandValidator : AbstractValidator<SubmitTravelCommand>
{
    private static readonly string[] AllowedTypes = { "domestic", "international" };

    public SubmitTravelCommandValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty();
        RuleFor(x => x.ApplicantUserId).NotEmpty();
        RuleFor(x => x.DestinationType)
            .NotEmpty()
            .Must(t => AllowedTypes.Contains(t))
            .WithMessage($"DestinationType must be one of: {string.Join(", ", AllowedTypes)}.");
        RuleFor(x => x.Destination).NotEmpty().MaximumLength(256);
        RuleFor(x => x.DepartDate).NotEqual(default(DateOnly));
        RuleFor(x => x.ReturnDate).GreaterThanOrEqualTo(x => x.DepartDate);
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.EstimatedCost)
            .GreaterThan(0)
            .LessThanOrEqualTo(1_000_000)
            .WithMessage("EstimatedCost must satisfy 0 < value <= 1,000,000");
    }
}

public sealed class SubmitTravelCommandHandler(
    IAppDbContext db,
    IClock clock,
    TravelApprovalResolver resolver,
    TravelNotificationEmitter emitter
) : IRequestHandler<SubmitTravelCommand, TravelCaseDto>
{
    public async Task<TravelCaseDto> Handle(SubmitTravelCommand request, CancellationToken ct)
    {
        var managerId = await resolver.ResolveManagerApproverAsync(request.ApplicantUserId, ct);

        var c = TravelCase.Submit(
            tenantCode: request.TenantCode,
            applicantUserId: request.ApplicantUserId,
            destinationType: request.DestinationType,
            destination: request.Destination,
            departDate: request.DepartDate,
            returnDate: request.ReturnDate,
            purpose: request.Purpose,
            estimatedCost: request.EstimatedCost,
            firstApproverUserId: managerId,
            now: clock.UtcNow);

        db.TravelCases.Add(c);
        await db.SaveChangesAsync(ct);

        await emitter.EmitOnAssignApproverAsync(c, caseUrl: $"/cases/travel/{c.Id}", ct);

        return TravelCaseDto.FromDomain(c);
    }
}
