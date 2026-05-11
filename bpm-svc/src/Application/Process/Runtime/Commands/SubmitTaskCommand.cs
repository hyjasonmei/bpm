using System.Text.Json;
using Bpm.Application.Common.Messaging;
using Bpm.Domain.Entities.Process;

namespace Bpm.Application.Process.Runtime.Commands;

public sealed record SubmitTaskCommand(
    Guid TaskId,
    Guid ActorUserId,
    JsonElement? FormDataPatch,
    Decision? Decision,
    string? Comment
) : ICommand;
