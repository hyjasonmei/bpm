using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

/// <summary>
/// Read/write surface for the Cook tab's two-way chat thread. Both
/// the user-facing HTTP API and the chef-facing MCP tools end up
/// here, so message persistence and ordering stay consistent
/// regardless of who posted.
/// </summary>
public interface IFlowChatService
{
    Task<IReadOnlyList<FlowChatMessage>> ListAsync(Guid flowId, DateTime? since, CancellationToken ct = default);

    Task<FlowChatMessage> AppendAsync(AppendChatMessageInput input, CancellationToken ct = default);
}

public sealed record AppendChatMessageInput(
    Guid FlowId,
    FlowChatSender Sender,
    FlowChatKind Kind,
    string Content,
    string? ArtifactsJson = null,
    string? Version = null,
    Guid? AuthorUserId = null);
