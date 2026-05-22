using Bpm.Application.Common.Messaging;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed record ReturnTaskCommand(
    Guid TaskId,
    Guid ActorUserId,
    string Comment
) : ICommand;
