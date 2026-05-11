using FluentValidation;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed class SubmitTaskCommandValidator : AbstractValidator<SubmitTaskCommand>
{
    public SubmitTaskCommandValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEqual(Guid.Empty).WithMessage("TaskId must be a non-empty Guid.");

        RuleFor(x => x.ActorUserId)
            .NotEqual(Guid.Empty).WithMessage("ActorUserId must be a non-empty Guid.");

        RuleFor(x => x.Comment)
            .MaximumLength(2000).WithMessage("Comment must be at most 2000 characters.")
            .When(x => x.Comment is not null);
    }
}
