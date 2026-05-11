using FluentValidation;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed class StartInstanceCommandValidator : AbstractValidator<StartInstanceCommand>
{
    public StartInstanceCommandValidator()
    {
        RuleFor(x => x.SpecCode)
            .NotEmpty().WithMessage("SpecCode must be non-empty.")
            .MaximumLength(128);

        RuleFor(x => x.InitiatorUserId)
            .NotEqual(Guid.Empty).WithMessage("InitiatorUserId must be a non-empty Guid.");
    }
}
