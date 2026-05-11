using System.Text.Json;
using Bpm.Domain.Entities.Process;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Application.Process.Runtime.Dtos;

/// <summary>
/// Read model for a single process instance, including the open-task summary
/// the UI needs to render the worklist row + detail header in one round trip.
/// </summary>
public sealed record ProcessInstanceDto(
    Guid Id,
    string SpecCode,
    int SpecVersion,
    Guid InitiatorUserId,
    InstanceStatus Status,
    JsonElement CurrentFormData,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    IReadOnlyList<ProcessTaskDto> OpenTasks);

public sealed record ProcessTaskDto(
    Guid Id,
    string NodeId,
    NodeKind NodeKind,
    Guid? OriginalAssigneeUserId,
    Guid? ActualAssigneeUserId,
    TaskStatus Status,
    DateTime? DueAt,
    DateTime? ClaimedAt,
    DateTime? CompletedAt,
    Decision? Decision,
    string? Comment);

public sealed record TaskHistoryDto(
    Guid Id,
    Guid? TaskId,
    HistoryEventType EventType,
    Guid? ActorUserId,
    JsonElement Payload,
    DateTime CreatedAt);

public sealed record HistoryPage(IReadOnlyList<TaskHistoryDto> Items, string? NextCursor);

/// <summary>
/// Returned by <c>GET /api/tasks/{id}</c>. The merged form data is the
/// instance's current form snapshot — the rendering form needs both the
/// task definition and the instance-wide accumulated data to show prefilled
/// fields from earlier steps.
/// </summary>
public sealed record TaskWithFormDto(
    ProcessTaskDto Task,
    JsonElement MergedFormData,
    string SpecCode,
    Guid InstanceId);

/// <summary>Request bodies for the controller surface.</summary>
public sealed record StartProcessRequest(string SpecCode, JsonElement FormData);
public sealed record CancelProcessRequest(string Reason);
public sealed record SubmitTaskRequest(JsonElement? FormDataPatch, string? Decision, string? Comment);
public sealed record ReturnTaskRequest(string Comment);
