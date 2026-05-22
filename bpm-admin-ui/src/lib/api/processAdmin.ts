/**
 * Process Admin REST client (PR-K1). Wraps the
 * `/api/admin/process-admin` surface in `ProcessAdminController.cs`.
 *
 * Style mirrors `lib/api/flowLibrary.ts`:
 *  - Each call returns a typed promise; non-2xx → throw with body.detail /
 *    body.error / body.title fallback.
 *  - `apiFetch` injects the JWT bearer.
 */

import { apiFetch } from '@/lib/apiFetch'
import type { SpecBundleStatus } from '@/types/flowLibrary'

export type DefinitionSource = 'bundle' | 'filesystem'

export interface FlowDefinitionDto {
  source: DefinitionSource
  flowCode: string
  version: number
  /** Bundle status (camelCase via JsonStringEnumConverter); null for filesystem rows. */
  status: string | null
  /** null for filesystem rows. */
  bundleId: string | null
  /** ISO timestamp; null when neither last-repro nor manifest exportedAt is available. */
  lastModifiedAt: string | null
}

export interface FlowVersionDto {
  bundleId: string
  flowVersion: number
  manifestChecksum: string
  exportedAt: string
  status: SpecBundleStatus
  lastReproCheckAt: string | null
}

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.error) detail = body.error
      else if (body?.title) detail = body.title
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.json() as T
}

export async function listDefinitions(tenantCode: string = 'default'): Promise<FlowDefinitionDto[]> {
  const res = await apiFetch(
    `/api/admin/process-admin/definitions?tenantCode=${encodeURIComponent(tenantCode)}`,
  )
  return jsonOrThrow<FlowDefinitionDto[]>(res)
}

export async function listVersions(flowCode: string, tenantCode: string = 'default'): Promise<FlowVersionDto[]> {
  const res = await apiFetch(
    `/api/admin/process-admin/definitions/${encodeURIComponent(flowCode)}/versions`
      + `?tenantCode=${encodeURIComponent(tenantCode)}`,
  )
  return jsonOrThrow<FlowVersionDto[]>(res)
}

/**
 * Raw spec.json text for a flowCode (PR-K2 §3.6 — Designer load path).
 * Backend prefers the latest non-deleted bundle; falls back to the
 * filesystem spec under SampleSpecsDir. Returns the spec verbatim.
 */
export async function getSpecJson(flowCode: string, tenantCode: string = 'default'): Promise<string> {
  const res = await apiFetch(
    `/api/admin/process-admin/definitions/${encodeURIComponent(flowCode)}/spec`
      + `?tenantCode=${encodeURIComponent(tenantCode)}`,
  )
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.error) detail = body.error
      else if (body?.detail) detail = body.detail
      else if (body?.title) detail = body.title
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.text()
}

/* ─── PR-K3: Process Simulator ─────────────────────────────────── */

/**
 * One row in the simulator's trace. `outcome` is one of
 *   - `auto-advanced` (synthetic node — start / gateway / notify / end)
 *   - `completed`     (task auto-submitted by the simulator)
 *   - `spawned`       (task open at simulation end)
 *   - `errored`       (submission failed; trace stops here)
 *
 * The UI colours the row by outcome — see `Simulator.tsx`.
 */
export interface SimulationStepDto {
  nodeId: string
  nodeKind: string
  outcome: 'auto-advanced' | 'completed' | 'spawned' | 'errored' | string
  resolvedAssigneeUserId: string | null
  assigneeName: string | null
  decision: string | null
  notes: string | null
}

export interface SimulationNotificationDto {
  notificationId: string
  trigger: string
  subject: string
  recipients: string[]
}

export interface SimulationResultDto {
  success: boolean
  /** Populated when `success === false`. */
  error: string | null
  trace: SimulationStepDto[]
  notifications: SimulationNotificationDto[]
  /** Final form data after the simulation; null when start failed. */
  finalFormData: unknown
  /** "Completed" / "Cancelled" / "Errored" / "Running"; null when start failed. */
  finalStatus: string | null
}

export interface SimulateRequestPayload {
  flowCode: string
  formData: unknown
  /** Reserved — no UI yet (v1 simulates against the live org chart). */
  sampleOrg?: null
  initiatorUserId?: string | null
}

export async function simulate(req: SimulateRequestPayload): Promise<SimulationResultDto> {
  const res = await apiFetch('/api/admin/process-admin/simulate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      flowCode: req.flowCode,
      formData: req.formData,
      sampleOrg: req.sampleOrg ?? null,
      initiatorUserId: req.initiatorUserId ?? null,
    }),
  })
  return jsonOrThrow<SimulationResultDto>(res)
}

/* ─── PR-K4: LiveCases monitoring ──────────────────────────────── */

/** Active (Running / Errored) instance row in the LiveCases table. */
export interface ActiveCaseDto {
  id: string
  specCode: string
  specVersion: number
  initiatorUserId: string
  initiatorEmail: string | null
  status: 'Running' | 'Completed' | 'Cancelled' | 'Errored' | string
  startedAt: string
  lastActivityAt: string
  openTaskCount: number
  hasBreach: boolean
  /** Set only when the case has exactly one open task. */
  currentAssigneeUserId: string | null
  currentAssigneeEmail: string | null
}

export interface ProcessTaskDto {
  id: string
  nodeId: string
  nodeKind: string
  originalAssigneeUserId: string | null
  actualAssigneeUserId: string | null
  status: string
  dueAt: string | null
  claimedAt: string | null
  completedAt: string | null
  decision: string | null
  comment: string | null
}

export interface ProcessInstanceDto {
  id: string
  specCode: string
  specVersion: number
  initiatorUserId: string
  status: string
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
  eventType: string
  actorUserId: string | null
  payload: unknown
  createdAt: string
}

export interface LiveCaseDetailDto {
  instance: ProcessInstanceDto
  recentHistory: TaskHistoryDto[]
}

export interface ActiveCasesQuery {
  specCode?: string | null
  /** 1 / 7 / 30 / null (= no age filter). */
  maxAgeDays?: number | null
  breachOnly?: boolean
  limit?: number
}

export async function listActiveCases(q: ActiveCasesQuery = {}): Promise<ActiveCaseDto[]> {
  const params = new URLSearchParams()
  if (q.specCode) params.set('specCode', q.specCode)
  if (q.maxAgeDays != null && q.maxAgeDays > 0) params.set('maxAgeDays', String(q.maxAgeDays))
  if (q.breachOnly) params.set('breachOnly', 'true')
  if (q.limit != null && q.limit > 0) params.set('limit', String(q.limit))
  const qs = params.toString()
  const res = await apiFetch(
    `/api/admin/process-admin/cases/active${qs ? `?${qs}` : ''}`,
  )
  return jsonOrThrow<ActiveCaseDto[]>(res)
}

export async function getCaseDetail(instanceId: string): Promise<LiveCaseDetailDto> {
  const res = await apiFetch(`/api/admin/process-admin/cases/${encodeURIComponent(instanceId)}`)
  return jsonOrThrow<LiveCaseDetailDto>(res)
}

/* ─── PR-K4: Admin intervention ───────────────────────────────── */

export interface ForceReassignBody {
  newAssigneeUserId: string
  reason: string
}

export interface ForceReturnBody {
  targetNodeId: string
  reason: string
}

export interface ForceSubmitBody {
  formDataPatch?: unknown
  decision?: 'Approve' | 'Reject' | 'Return' | null
  reason: string
}

export interface TerminateBody {
  reason: string
}

async function postOk(path: string, body: unknown): Promise<void> {
  const res = await apiFetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const data = await res.json()
      if (data?.detail) detail = data.detail
      else if (data?.error) detail = data.error
      else if (data?.title) detail = data.title
      else if (data?.errors) detail = JSON.stringify(data.errors)
    } catch { /* ignore */ }
    throw new Error(detail)
  }
}

export const forceReassign = (taskId: string, body: ForceReassignBody) =>
  postOk(`/api/admin/process-admin/tasks/${encodeURIComponent(taskId)}/force-reassign`, body)

export const forceReturn = (taskId: string, body: ForceReturnBody) =>
  postOk(`/api/admin/process-admin/tasks/${encodeURIComponent(taskId)}/force-return`, body)

export const forceSubmit = (taskId: string, body: ForceSubmitBody) =>
  postOk(`/api/admin/process-admin/tasks/${encodeURIComponent(taskId)}/force-submit`, body)

export const terminateInstance = (instanceId: string, body: TerminateBody) =>
  postOk(`/api/admin/process-admin/processes/${encodeURIComponent(instanceId)}/terminate`, body)

/* ─── PR-K5 §7.1: Completed Cases ──────────────────────────────── */

export interface CompletedCaseDto {
  id: string
  specCode: string
  specVersion: number
  initiatorUserId: string
  initiatorEmail: string | null
  status: 'Completed' | 'Cancelled' | string
  startedAt: string
  completedAt: string | null
  cancelledAt: string | null
  /** Backend writes this as `00:34:21.5` (TimeSpan). */
  cycleTime: string | null
  cancelReason: string | null
}

export interface CompletedCasesPage {
  items: CompletedCaseDto[]
  nextCursor: string | null
}

export interface CompletedCasesQuery {
  specCode?: string | null
  /** Filter to instances with completedAt OR cancelledAt at/after this UTC ISO string. */
  completedAfter?: string | null
  /** "completed" / "cancelled" / null (= both). */
  status?: 'completed' | 'cancelled' | null
  limit?: number
  cursor?: string | null
}

export async function listCompletedCases(q: CompletedCasesQuery = {}): Promise<CompletedCasesPage> {
  const params = new URLSearchParams()
  if (q.specCode) params.set('specCode', q.specCode)
  if (q.completedAfter) params.set('completedAfter', q.completedAfter)
  if (q.status) params.set('status', q.status)
  if (q.limit != null && q.limit > 0) params.set('limit', String(q.limit))
  if (q.cursor) params.set('cursor', q.cursor)
  const qs = params.toString()
  const res = await apiFetch(
    `/api/admin/process-admin/cases/completed${qs ? `?${qs}` : ''}`,
  )
  return jsonOrThrow<CompletedCasesPage>(res)
}

/* ─── PR-K5 §8.4: Reports ──────────────────────────────────────── */

export type ReportPeriodToken = '7d' | '30d' | '90d' | 'all'

export interface NodeBottleneckDto {
  nodeId: string
  avgWaitTimeHours: number
  completedCount: number
}

export interface AssigneeLoadDto {
  userId: string
  email: string | null
  openTasks: number
  completedTasks: number
  avgTaskTimeHours: number
}

export interface CycleTimeBucketDto {
  range: string
  count: number
}

export interface PerSpecReportDto {
  specCode: string
  /** Backend serializes the enum as the int value (Last7Days=7, etc.) by default. */
  period: number | string
  totalStarted: number
  totalCompleted: number
  totalCancelled: number
  totalRunning: number
  avgCycleTimeHours: number
  p50CycleTimeHours: number
  p90CycleTimeHours: number
  breachCount: number
  breachRate: number
  bottlenecks: NodeBottleneckDto[]
  assigneeLoads: AssigneeLoadDto[]
  cycleTimeHistogram: CycleTimeBucketDto[]
}

export async function getPerSpecReport(specCode: string, period: ReportPeriodToken = '30d'): Promise<PerSpecReportDto> {
  const params = new URLSearchParams({ specCode, period })
  const res = await apiFetch(`/api/admin/process-admin/reports/per-spec?${params}`)
  return jsonOrThrow<PerSpecReportDto>(res)
}

/* ─── Phase 2.1b: NotificationDispatchAudits ──────────────────── */

export type NotificationAuditStatus = 'captured' | 'dispatched' | 'failed'

export interface NotificationAuditDto {
  id: string
  instanceId: string
  taskId: string | null
  specCode: string
  trigger: string
  notificationId: string
  channel: string | null
  recipient: string | null
  subject: string | null
  body: string | null
  status: NotificationAuditStatus
  error: string | null
  dispatchedAt: string
}

export interface NotificationAuditQuery {
  specCode?: string
  status?: NotificationAuditStatus
  instanceId?: string
  limit?: number
}

export async function listNotificationAudits(q: NotificationAuditQuery = {}): Promise<NotificationAuditDto[]> {
  const params = new URLSearchParams()
  if (q.specCode) params.set('specCode', q.specCode)
  if (q.status) params.set('status', q.status)
  if (q.instanceId) params.set('instanceId', q.instanceId)
  if (q.limit != null && q.limit > 0) params.set('limit', String(q.limit))
  const qs = params.toString()
  const res = await apiFetch(`/api/admin/process-admin/notification-audits${qs ? `?${qs}` : ''}`)
  return jsonOrThrow<NotificationAuditDto[]>(res)
}
