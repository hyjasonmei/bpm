import { api } from '@/flowcook/api'

export type FlowState =
  | 'Draft'
  | 'Submitted'
  | 'Cooking'
  | 'OnHold'
  | 'Committed'
  | 'Approved'
  | 'Rejected'
  | 'Retired'

export interface FlowSummary {
  id: string
  lineageId: string
  version: number
  state: FlowState
  flowCode: string
  displayName: string
  createdAt: string
  updatedAt: string
  /** Last chef MCP call touch (PR-K1). Drives the list-page stall pill. */
  lastChefHeartbeatAt: string | null
  /** Assigned launcher group, or null when unassigned (PR-G1). */
  groupId: string | null
  /** Denormalised group code so the list row can chip without a second fetch. */
  groupCode: string | null
  /** Curated lucide icon name for the bpm launcher tile, or null (default icon). */
  iconKey: string | null
  /** Launcher sort weight within the flow's group (low → high; ties on flowCode). */
  displayOrder: number
  /** Free-form JSON about chef's workspace (PR-W1). Today shape:
   *  { branch, notes?, setAt }. Null when no chef session has set it. */
  chefWorkContextJson: string | null
}

export interface FlowDetail extends FlowSummary {
  specJson: string
  notes: string | null
  createdByUserId: string | null
}

/** Parse the chef work-context JSON helper; tolerant of nulls / garbage. */
export interface ChefWorkContext {
  branch?: string
  notes?: string
  setAt?: string
}
export function parseChefWorkContext(json: string | null | undefined): ChefWorkContext | null {
  if (!json) return null
  try {
    const parsed = JSON.parse(json) as ChefWorkContext
    if (!parsed.branch) return null
    return parsed
  } catch { return null }
}

export interface CreateFlowRequest {
  flowCode: string
  displayName: string
  specJson?: string
}

export interface UpdateFlowSpecRequest {
  specJson: string
  flowCode?: string
  displayName?: string
}

export function listFlows(state?: FlowState, lineageId?: string): Promise<FlowSummary[]> {
  const params = new URLSearchParams()
  if (state) params.set('state', state)
  if (lineageId) params.set('lineageId', lineageId)
  const qs = params.toString()
  return api<FlowSummary[]>(`/api/flows${qs ? `?${qs}` : ''}`)
}

export function getFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}`)
}

export function createFlow(req: CreateFlowRequest): Promise<FlowDetail> {
  return api<FlowDetail>('/api/flows', { method: 'POST', json: req })
}

export function updateFlowSpec(id: string, req: UpdateFlowSpecRequest): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/spec`, { method: 'PUT', json: req })
}

export function submitFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/submit`, { method: 'POST' })
}

export function cancelFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/cancel`, { method: 'POST' })
}

export function resumeFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/resume`, { method: 'POST' })
}

export function cloneFlowVersion(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/clone-version`, { method: 'POST' })
}

export function retireFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/retire`, { method: 'POST' })
}

export function unretireFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/unretire`, { method: 'POST' })
}

export function deleteFlow(id: string): Promise<void> {
  return api<void>(`/api/flows/${id}`, { method: 'DELETE' })
}

/** Set (or clear, with `iconKey: null`) the launcher icon. Curated lucide name. */
export function setFlowIcon(id: string, iconKey: string | null): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/icon`, { method: 'POST', json: { iconKey } })
}

/** Persist launcher order: `flowIds` in the desired order; each row's
 *  displayOrder is set server-side to its index in this list. */
export function reorderFlows(flowIds: string[]): Promise<void> {
  return api<void>('/api/flows/reorder', { method: 'POST', json: { flowIds } })
}
