using System.Text.Json;
using Bpm.Application.Common.Messaging;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed record StartInstanceResult(Guid InstanceId, Guid FirstTaskId);

public sealed record StartInstanceCommand(
    string SpecCode,
    JsonElement FormData,
    Guid InitiatorUserId
) : ICommand<StartInstanceResult>;
