// Mirrors Bpm.Application.Process.Runtime.Dtos.* and the underlying
// Bpm.Domain.Entities.Process.* enums. Property names here match the
// camelCase JSON shape ASP.NET Core's default System.Text.Json options
// produce for the C# records.
//
// Wire format note: the backend has no JsonStringEnumConverter registered,
// so the C# enums travel as **numeric** values (1-based, matching the C#
// declaration order). The string literal unions below are the ergonomic
// public shape — `process.ts` (the API client) converts at the boundary
// using the *Code maps so screens never need to remember which int means
// which state.

export type InstanceStatus = 'Running' | 'Completed' | 'Cancelled' | 'Errored'

export const InstanceStatusCode = {
  Running: 1,
  Completed: 2,
  Cancelled: 3,
  Errored: 4,
} as const satisfies Record<InstanceStatus, number>

export type TaskStatus = 'Pending' | 'InProgress' | 'Completed' | 'Cancelled' | 'Skipped'

export const TaskStatusCode = {
  Pending: 1,
  InProgress: 2,
  Completed: 3,
  Cancelled: 4,
  Skipped: 5,
} as const satisfies Record<TaskStatus, number>

export type NodeKind =
  | 'StartEvent'
  | 'EndEvent'
  | 'UserTask'
  | 'Approval'
  | 'Gateway'
  | 'Notify'
  | 'ServiceTask'

export const NodeKindCode = {
  StartEvent: 1,
  EndEvent: 2,
  UserTask: 3,
  Approval: 4,
  Gateway: 5,
  Notify: 6,
  ServiceTask: 7,
} as const satisfies Record<NodeKind, number>

export type Decision = 'Approve' | 'Reject' | 'Return'

export const DecisionCode = {
  Approve: 1,
  Reject: 2,
  Return: 3,
} as const satisfies Record<Decision, number>

export type HistoryEventType =
  | 'InstanceStarted'
  | 'TaskSpawned'
  | 'TaskClaimed'
  | 'TaskSubmitted'
  | 'TaskReturned'
  | 'ApprovalApproved'
  | 'ApprovalRejected'
  | 'NotificationDispatched'
  | 'NotificationCaptured'
  | 'GatewayEvaluated'
  | 'InstanceCompleted'
  | 'InstanceCancelled'
  | 'DelegationApplied'
  | 'SlaWarning'
  | 'SlaBreached'

export const HistoryEventTypeCode = {
  InstanceStarted: 1,
  TaskSpawned: 2,
  TaskClaimed: 3,
  TaskSubmitted: 4,
  TaskReturned: 5,
  ApprovalApproved: 6,
  ApprovalRejected: 7,
  NotificationDispatched: 8,
  NotificationCaptured: 9,
  GatewayEvaluated: 10,
  InstanceCompleted: 11,
  InstanceCancelled: 12,
  DelegationApplied: 13,
  SlaWarning: 14,
  SlaBreached: 15,
} as const satisfies Record<HistoryEventType, number>

// Reverse maps used by the API client to translate wire-numbers back to
// the literal string names. Built from the *Code maps to keep them
// authoritative.
export const InstanceStatusByCode: Record<number, InstanceStatus> = Object.fromEntries(
  Object.entries(InstanceStatusCode).map(([k, v]) => [v, k as InstanceStatus]),
)
export const TaskStatusByCode: Record<number, TaskStatus> = Object.fromEntries(
  Object.entries(TaskStatusCode).map(([k, v]) => [v, k as TaskStatus]),
)
export const NodeKindByCode: Record<number, NodeKind> = Object.fromEntries(
  Object.entries(NodeKindCode).map(([k, v]) => [v, k as NodeKind]),
)
export const DecisionByCode: Record<number, Decision> = Object.fromEntries(
  Object.entries(DecisionCode).map(([k, v]) => [v, k as Decision]),
)
export const HistoryEventTypeByCode: Record<number, HistoryEventType> = Object.fromEntries(
  Object.entries(HistoryEventTypeCode).map(([k, v]) => [v, k as HistoryEventType]),
)

// DTO shapes — identical to the C# records modulo enum conversion. Times
// are ISO-8601 strings (System.Text.Json's default for DateTime).

export interface ProcessTaskDto {
  id: string
  nodeId: string
  nodeKind: NodeKind
  originalAssigneeUserId: string | null
  actualAssigneeUserId: string | null
  status: TaskStatus
  dueAt: string | null
  claimedAt: string | null
  completedAt: string | null
  decision: Decision | null
  comment: string | null
  // PR-L3: owning instance's spec code, surfaced so the inbox can route a
  // task click to the matching FormCode without a per-row instance fetch.
  specCode: string
}

export interface ProcessInstanceDto {
  id: string
  specCode: string
  specVersion: number
  initiatorUserId: string
  status: InstanceStatus
  // The server sends the merged form snapshot as a JsonElement; on the
  // wire that's just an arbitrary JSON value, modelled as `unknown` so
  // callers cast / shape-check inside form components.
  currentFormData: unknown
  startedAt: string
  completedAt: string | null
  cancelledAt: string | null
  cancelReason: string | null
  openTasks: ProcessTaskDto[]
}

export interface TaskHistoryDto {
  id: string
  taskId: string | null
  eventType: HistoryEventType
  actorUserId: string | null
  payload: unknown
  createdAt: string
}

export interface HistoryPage {
  items: TaskHistoryDto[]
  nextCursor: string | null
}

export interface TaskWithFormDto {
  task: ProcessTaskDto
  mergedFormData: unknown
  specCode: string
  instanceId: string
}

// PR-L3: trimmed-down sibling of ProcessInstanceDto for the Home "My
// recent cases" panel + the Search screen's user-scoped query. Mirrors
// Bpm.Application.Process.Runtime.Dtos.MyInstanceSummaryDto.
export interface MyInstanceSummaryDto {
  id: string
  specCode: string
  specVersion: number
  status: InstanceStatus
  startedAt: string
  completedAt: string | null
  lastActivityAt: string
  openTaskCount: number
  currentNodeLabel: string | null
}
