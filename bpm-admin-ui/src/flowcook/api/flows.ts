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
}

export interface FlowDetail extends FlowSummary {
  specJson: string
  notes: string | null
  createdByUserId: string | null
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
