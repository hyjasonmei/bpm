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
    string? ChefWorkContextJson);

public record CreateFlowRequest(string FlowCode, string DisplayName, string? SpecJson);

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
