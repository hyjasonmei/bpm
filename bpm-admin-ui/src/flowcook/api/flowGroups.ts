import { api } from '@/flowcook/api'

export interface FlowGroupDto {
  id: string
  code: string
  displayName: Record<string, string>
  sortOrder: number
  icon: string | null
  flowCount: number
}

export interface CreateFlowGroupRequest {
  code: string
  displayName: Record<string, string>
  sortOrder: number
  icon?: string | null
}

export interface UpdateFlowGroupRequest {
  code?: string
  displayName?: Record<string, string>
  sortOrder?: number
  icon?: string | null
}

export function listFlowGroups(): Promise<FlowGroupDto[]> {
  return api<FlowGroupDto[]>('/api/flow-groups')
}

export function createFlowGroup(req: CreateFlowGroupRequest): Promise<FlowGroupDto> {
  return api<FlowGroupDto>('/api/flow-groups', { method: 'POST', json: req })
}

export function updateFlowGroup(id: string, req: UpdateFlowGroupRequest): Promise<FlowGroupDto> {
  return api<FlowGroupDto>(`/api/flow-groups/${id}`, { method: 'PUT', json: req })
}

export function deleteFlowGroup(id: string): Promise<void> {
  return api<void>(`/api/flow-groups/${id}`, { method: 'DELETE' })
}

export function assignFlowGroup(flowId: string, groupId: string | null): Promise<unknown> {
  return api<unknown>(`/api/flows/${flowId}/group`, { method: 'POST', json: { groupId } })
}
