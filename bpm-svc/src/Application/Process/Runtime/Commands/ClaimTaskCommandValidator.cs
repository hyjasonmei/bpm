using FluentValidation;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed class ClaimTaskCommandValidator : AbstractValidator<ClaimTaskCommand>
{
    public ClaimTaskCommandValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEqual(Guid.Empty).WithMessage("TaskId must be a non-empty Guid.");

        RuleFor(x => x.ActorUserId)
            .NotEqual(Guid.Empty).WithMessage("ActorUserId must be a non-empty Guid.");
    }
}
