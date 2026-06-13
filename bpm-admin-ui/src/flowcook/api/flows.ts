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
  | 'Published'

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
  /** PR the chef agent opened for this flow's cook branch (PR-CA1), or null. */
  prUrl: string | null
  /** When the cook branch was confirmed merged to main; Publish is blocked while null (PR-CA1). */
  mergedAt: string | null
}

export interface FlowDetail extends FlowSummary {
  specJson: string
  notes: string | null
  createdByUserId: string | null
  /** Canonical BPMN XML for flows registered from shipped code (no spec).
   *  Lets the SOURCE step show a read-only diagram when specJson is empty. */
  bpmnXml: string | null
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

// Two-stage go-live: approve (reviewed) → publish (live in this env) ⇄ unpublish.
export function approveFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/approve`, { method: 'POST' })
}
export function publishFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/publish`, { method: 'POST' })
}
export function unpublishFlow(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/unpublish`, { method: 'POST' })
}
/** Manual "Mark merged" escape hatch (PR-CA1) — unblocks Publish when merge
 *  auto-detection can't fire (e.g. squash merge in a remote-less env). */
export function markMerged(id: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/mark-merged`, { method: 'POST' })
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

/** Rename a flow's display label only — allowed in any state (it's a
 *  label, not behaviour). Server also syncs the spec's meta.flowName. */
export function renameFlow(id: string, displayName: string): Promise<FlowDetail> {
  return api<FlowDetail>(`/api/flows/${id}/display-name`, { method: 'PATCH', json: { displayName } })
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
