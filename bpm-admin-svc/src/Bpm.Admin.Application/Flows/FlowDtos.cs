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
    DateTime UpdatedAt);

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
    DateTime UpdatedAt);

public record CreateFlowRequest(string FlowCode, string DisplayName, string? SpecJson);

public record UpdateFlowSpecRequest(string SpecJson, string? FlowCode, string? DisplayName);

public record OnHoldRequest(string Question);
