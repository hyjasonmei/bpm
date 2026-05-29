import { api } from '@/flowcook/api'

export interface EnvironmentDto {
  id: string
  code: string
  displayName: string
  sortOrder: number
}

export interface CreateEnvironmentRequest {
  code: string
  displayName: string
  sortOrder: number
}

export interface UpdateEnvironmentRequest {
  code?: string
  displayName?: string
  sortOrder?: number
}

export function listEnvironments(): Promise<EnvironmentDto[]> {
  return api<EnvironmentDto[]>('/api/environments')
}

export function createEnvironment(req: CreateEnvironmentRequest): Promise<EnvironmentDto> {
  return api<EnvironmentDto>('/api/environments', { method: 'POST', json: req })
}

export function updateEnvironment(id: string, req: UpdateEnvironmentRequest): Promise<EnvironmentDto> {
  return api<EnvironmentDto>(`/api/environments/${id}`, { method: 'PUT', json: req })
}

export function deleteEnvironment(id: string): Promise<void> {
  return api<void>(`/api/environments/${id}`, { method: 'DELETE' })
}

// ── per-flow deployment status board ─────────────────────────────────

export type FlowDeploymentStatus = 'NotDeployed' | 'Deployed'

export interface FlowDeploymentDto {
  id: string | null
  flowId: string
  environmentId: string
  environmentCode: string
  environmentDisplayName: string
  environmentSortOrder: number
  status: FlowDeploymentStatus
  deployedAt: string | null
  deployedByUserId: string | null
  notes: string | null
}

export function listFlowDeployments(flowId: string): Promise<FlowDeploymentDto[]> {
  return api<FlowDeploymentDto[]>(`/api/flows/${flowId}/deployments`)
}

export function setFlowDeployment(
  flowId: string,
  envId: string,
  body: { status: FlowDeploymentStatus; notes?: string | null },
): Promise<FlowDeploymentDto> {
  return api<FlowDeploymentDto>(`/api/flows/${flowId}/deployments/${envId}`, { method: 'POST', json: body })
}

export function approveFlow(id: string): Promise<unknown> {
  return api<unknown>(`/api/flows/${id}/approve`, { method: 'POST' })
}
