using FluentValidation;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed class ReturnTaskCommandValidator : AbstractValidator<ReturnTaskCommand>
{
    public ReturnTaskCommandValidator()
    {
        RuleFor(x => x.TaskId)
            .NotEqual(Guid.Empty).WithMessage("TaskId must be a non-empty Guid.");

        RuleFor(x => x.ActorUserId)
            .NotEqual(Guid.Empty).WithMessage("ActorUserId must be a non-empty Guid.");

        RuleFor(x => x.Comment)
            .NotEmpty().WithMessage("Comment is required when returning a task.")
            .MaximumLength(2000).WithMessage("Comment must be at most 2000 characters.");
    }
}
