using Bpm.Application.Common.Messaging;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed record CancelInstanceCommand(
    Guid InstanceId,
    Guid ActorUserId,
    string Reason
) : ICommand;
