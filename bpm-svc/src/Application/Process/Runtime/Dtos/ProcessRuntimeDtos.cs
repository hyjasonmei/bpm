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
    string? Comment,
    // PR-L3: surface the owning instance's spec code so the UI inbox can
    // route a task click straight to the matching FormCode without an extra
    // GetInstance round-trip.
    string SpecCode);

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

/// <summary>
/// PR-K4 §5.1 — admin-wide live monitor row. Joins ProcessInstance against
/// the User table to surface the initiator's email + the current single
/// assignee's email when there's exactly one open task. Larger fan-out is
/// signalled by <see cref="OpenTaskCount"/> &gt; 1 with a null
/// <see cref="CurrentAssigneeUserId"/>.
/// </summary>
public sealed record ActiveCaseDto(
    Guid Id,
    string SpecCode,
    int SpecVersion,
    Guid InitiatorUserId,
    string? InitiatorEmail,
    InstanceStatus Status,
    DateTime StartedAt,
    DateTime LastActivityAt,
    int OpenTaskCount,
    bool HasBreach,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeEmail);

/// <summary>
/// PR-K4 §5.2 — single-case detail bundle for the LiveCaseDetail screen.
/// Wraps the existing <see cref="ProcessInstanceDto"/> (header + open
/// tasks) with the most recent N history entries so one network round-trip
/// fills the whole drawer.
/// </summary>
public sealed record LiveCaseDetailDto(
    ProcessInstanceDto Instance,
    IReadOnlyList<TaskHistoryDto> RecentHistory);

/// <summary>
/// PR-K5 §7.1 — terminal (Completed / Cancelled) instance row for the
/// CompletedCases monitor. Includes a derived <see cref="CycleTime"/>
/// (CompletedAt or CancelledAt minus StartedAt) so the table can render
/// a friendly "2h 14m" / "3d 5h" without a second pass.
/// </summary>
public sealed record CompletedCaseDto(
    Guid Id,
    string SpecCode,
    int SpecVersion,
    Guid InitiatorUserId,
    string? InitiatorEmail,
    InstanceStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    TimeSpan? CycleTime,
    string? CancelReason);

public sealed record CompletedCasesPage(
    IReadOnlyList<CompletedCaseDto> Items,
    string? NextCursor);

/// <summary>
/// PR-L3 — initiator-scoped instance summary for the Home / Search "my cases"
/// list. Trimmed-down sibling of <see cref="ActiveCaseDto"/>: no breach flag /
/// assignee join (those are admin-board concerns), but adds
/// <see cref="CompletedAt"/> and <see cref="CurrentNodeLabel"/> so the UI can
/// render a status timeline + "where is my case sitting now" hint without
/// loading the full instance.
/// </summary>
public sealed record MyInstanceSummaryDto(
    Guid Id,
    string SpecCode,
    int SpecVersion,
    InstanceStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTime LastActivityAt,
    int OpenTaskCount,
    string? CurrentNodeLabel);
