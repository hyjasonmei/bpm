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
