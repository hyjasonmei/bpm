using System.Text.Json;
using Bpm.Domain.Entities.HrFlows;

namespace Bpm.Application.HrFlows.Dtos;

public sealed record HrFlowInstanceDto(
    Guid Id,
    HrFlowSpecCode SpecCode,
    Guid InitiatorUserId,
    string InitiatorName,
    Guid ResolvedManagerUserId,
    string ManagerName,
    HrFlowStatus Status,
    HrFlowStep CurrentStep,
    JsonElement FormData,
    DateTime StartedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    IReadOnlyList<HrFlowActionDto> Actions);

public sealed record HrFlowActionDto(
    Guid Id,
    Guid ActorUserId,
    string ActorName,
    HrFlowActionType Action,
    HrFlowStep FromStep,
    HrFlowStep ToStep,
    string? Comment,
    DateTime CreatedAt);

public sealed record HrFlowSummaryDto(
    Guid Id,
    HrFlowSpecCode SpecCode,
    Guid InitiatorUserId,
    string InitiatorName,
    HrFlowStatus Status,
    HrFlowStep CurrentStep,
    DateTime StartedAt,
    DateTime LastActivityAt);

public sealed record StartHrFlowRequest(JsonElement FormData);
public sealed record ApproveRequest(string? Comment);
public sealed record ReturnRequest(string Comment);
public sealed record ResubmitRequest(JsonElement FormData);
