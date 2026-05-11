using FluentValidation;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed class CancelInstanceCommandValidator : AbstractValidator<CancelInstanceCommand>
{
    public CancelInstanceCommandValidator()
    {
        RuleFor(x => x.InstanceId)
            .NotEqual(Guid.Empty).WithMessage("InstanceId must be a non-empty Guid.");

        RuleFor(x => x.ActorUserId)
            .NotEqual(Guid.Empty).WithMessage("ActorUserId must be a non-empty Guid.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required when cancelling an instance.")
            .MaximumLength(2000).WithMessage("Reason must be at most 2000 characters.");
    }
}
