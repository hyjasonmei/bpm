using Bpm.Application.Common.Messaging;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed record ClaimTaskCommand(
    Guid TaskId,
    Guid ActorUserId
) : ICommand;
