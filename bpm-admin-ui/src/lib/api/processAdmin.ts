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
