using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

public record FlowSummaryDto(
    Guid Id,
    Guid LineageId,
    int Version,
    FlowState State,
    string FlowCode,
    string DisplayName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastChefHeartbeatAt,
    Guid? GroupId,
    string? GroupCode,
    string? IconKey,
    int DisplayOrder,
    string? ChefWorkContextJson);

public record FlowDetailDto(
    Guid Id,
    Guid LineageId,
    int Version,
    FlowState State,
    string FlowCode,
    string DisplayName,
    string SpecJson,
    string? Notes,
    Guid? CreatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastChefHeartbeatAt,
    Guid? GroupId,
    string? GroupCode,
    string? IconKey,
    int DisplayOrder,
    string? ChefWorkContextJson,
    string? BpmnXml);

public record CreateFlowRequest(string FlowCode, string DisplayName, string? SpecJson);

public record RegisterShippedRequest(List<ShippedFlowInput> Flows);

/// <summary>Set or clear (<c>IconKey == null</c>) the launcher icon.</summary>
public record SetFlowIconRequest(string? IconKey);

/// <summary>
/// Reorder launcher tiles: <c>FlowIds</c> in the desired display order.
/// The service writes each row's <c>DisplayOrder</c> to its index.
/// </summary>
public record ReorderFlowsRequest(IReadOnlyList<Guid> FlowIds);

public record UpdateFlowSpecRequest(string SpecJson, string? FlowCode, string? DisplayName);

public record OnHoldRequest(string Question);

// ── PR-K1: chef chat / lifecycle DTOs ─────────────────────────────────

public record FlowChatMessageDto(
    Guid Id,
    Guid FlowId,
    string Sender,
    string Kind,
    string Content,
    string? ArtifactsJson,
    string? Version,
    DateTime CreatedAt,
    Guid? AuthorUserId);

public record ChefAppendMessageRequest(
    string Kind,
    string Content,
    string? ArtifactsJson,
    string? Version);

public record ChefTransitionRequest(
    string Target,
    string? Question,
    string? Reason);

public record UserChatReplyRequest(
    string Kind,
    string Content);
