import { apiFetch } from '@/lib/apiFetch'
import type {
  Decision,
  HistoryEventType,
  HistoryPage,
  InstanceStatus,
  NodeKind,
  ProcessInstanceDto,
  ProcessTaskDto,
  TaskHistoryDto,
  TaskStatus,
  TaskWithFormDto,
} from '@/types/process'
import {
  DecisionByCode,
  HistoryEventTypeByCode,
  InstanceStatusByCode,
  NodeKindByCode,
  TaskStatusByCode,
} from '@/types/process'

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.title) detail = body.title
      else if (body?.error) detail = body.error
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  // Some endpoints (claim / submit / return / cancel) return 204; callers
  // typed as Promise<void> never await jsonOrThrow on those paths.
  return await res.json() as T
}

async function voidOrThrow(res: Response): Promise<void> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.title) detail = body.title
      else if (body?.error) detail = body.error
    } catch { /* ignore */ }
    throw new Error(detail)
  }
}

// ── enum normalization ────────────────────────────────────────────────
// The server's default System.Text.Json options emit enums as numbers
// (1, 2, 3 …). The types in `types/process.ts` expose them as string
// literals so screens can switch/discriminate without remembering codes.
// These helpers translate at the wire boundary.

function normInstanceStatus(v: unknown): InstanceStatus {
  if (typeof v === 'string') return v as InstanceStatus
  if (typeof v === 'number') return InstanceStatusByCode[v]
  throw new Error(`invalid InstanceStatus: ${String(v)}`)
}
function normTaskStatus(v: unknown): TaskStatus {
  if (typeof v === 'string') return v as TaskStatus
  if (typeof v === 'number') return TaskStatusByCode[v]
  throw new Error(`invalid TaskStatus: ${String(v)}`)
}
function normNodeKind(v: unknown): NodeKind {
  if (typeof v === 'string') return v as NodeKind
  if (typeof v === 'number') return NodeKindByCode[v]
  throw new Error(`invalid NodeKind: ${String(v)}`)
}
function normDecision(v: unknown): Decision | null {
  if (v === null || v === undefined) return null
  if (typeof v === 'string') return v as Decision
  if (typeof v === 'number') return DecisionByCode[v]
  throw new Error(`invalid Decision: ${String(v)}`)
}
function normHistoryEventType(v: unknown): HistoryEventType {
  if (typeof v === 'string') return v as HistoryEventType
  if (typeof v === 'number') return HistoryEventTypeByCode[v]
  throw new Error(`invalid HistoryEventType: ${String(v)}`)
}

interface RawProcessTaskDto extends Omit<ProcessTaskDto, 'nodeKind' | 'status' | 'decision'> {
  nodeKind: NodeKind | number
  status: TaskStatus | number
  decision: Decision | number | null
}
function normTask(raw: RawProcessTaskDto): ProcessTaskDto {
  return {
    ...raw,
    nodeKind: normNodeKind(raw.nodeKind),
    status: normTaskStatus(raw.status),
    decision: normDecision(raw.decision),
  }
}

interface RawProcessInstanceDto extends Omit<ProcessInstanceDto, 'status' | 'openTasks'> {
  status: InstanceStatus | number
  openTasks: RawProcessTaskDto[]
}
function normInstance(raw: RawProcessInstanceDto): ProcessInstanceDto {
  return {
    ...raw,
    status: normInstanceStatus(raw.status),
    openTasks: (raw.openTasks ?? []).map(normTask),
  }
}

interface RawTaskHistoryDto extends Omit<TaskHistoryDto, 'eventType'> {
  eventType: HistoryEventType | number
}
function normHistoryItem(raw: RawTaskHistoryDto): TaskHistoryDto {
  return { ...raw, eventType: normHistoryEventType(raw.eventType) }
}

// ── endpoints ─────────────────────────────────────────────────────────

export async function startProcess(
  specCode: string,
  formData: unknown,
): Promise<{ instanceId: string; firstTaskId: string }> {
  const res = await apiFetch('/api/processes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ specCode, formData }),
  })
  return jsonOrThrow<{ instanceId: string; firstTaskId: string }>(res)
}

export async function getInstance(id: string): Promise<ProcessInstanceDto> {
  const res = await apiFetch(`/api/processes/${id}`)
  const raw = await jsonOrThrow<RawProcessInstanceDto>(res)
  return normInstance(raw)
}

export async function getInstanceHistory(
  id: string,
  cursor?: string,
  limit?: number,
): Promise<HistoryPage> {
  const qs = new URLSearchParams()
  if (cursor) qs.set('cursor', cursor)
  if (limit !== undefined) qs.set('limit', String(limit))
  const tail = qs.toString() ? `?${qs.toString()}` : ''
  const res = await apiFetch(`/api/processes/${id}/history${tail}`)
  const raw = await jsonOrThrow<{ items: RawTaskHistoryDto[]; nextCursor: string | null }>(res)
  return { items: raw.items.map(normHistoryItem), nextCursor: raw.nextCursor }
}

export async function cancelInstance(id: string, reason: string): Promise<void> {
  const res = await apiFetch(`/api/processes/${id}/cancel`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ reason }),
  })
  await voidOrThrow(res)
}

export async function myTasks(
  status: 'open' | 'completed' | 'all' = 'open',
  limit?: number,
): Promise<ProcessTaskDto[]> {
  const qs = new URLSearchParams({ status })
  if (limit !== undefined) qs.set('limit', String(limit))
  const res = await apiFetch(`/api/tasks/mine?${qs.toString()}`)
  const raw = await jsonOrThrow<RawProcessTaskDto[]>(res)
  return raw.map(normTask)
}

export async function getTask(id: string): Promise<TaskWithFormDto> {
  const res = await apiFetch(`/api/tasks/${id}`)
  const raw = await jsonOrThrow<{
    task: RawProcessTaskDto
    mergedFormData: unknown
    specCode: string
    instanceId: string
  }>(res)
  return {
    task: normTask(raw.task),
    mergedFormData: raw.mergedFormData,
    specCode: raw.specCode,
    instanceId: raw.instanceId,
  }
}

export async function claimTask(id: string): Promise<void> {
  const res = await apiFetch(`/api/tasks/${id}/claim`, { method: 'POST' })
  await voidOrThrow(res)
}

export async function submitTask(
  id: string,
  body: { formDataPatch?: unknown; decision?: Decision; comment?: string },
): Promise<void> {
  const res = await apiFetch(`/api/tasks/${id}/submit`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      formDataPatch: body.formDataPatch ?? null,
      decision: body.decision ?? null,
      comment: body.comment ?? null,
    }),
  })
  await voidOrThrow(res)
}

export async function returnTask(id: string, comment: string): Promise<void> {
  const res = await apiFetch(`/api/tasks/${id}/return`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ comment }),
  })
  await voidOrThrow(res)
}
