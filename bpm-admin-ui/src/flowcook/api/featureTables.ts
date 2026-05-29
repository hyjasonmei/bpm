import { api } from '@/flowcook/api'

export type FeatureTableStatus = 'Linked' | 'Orphan' | 'Archived' | 'Dangling'

export interface FeatureTableGroupDto {
  flowCode: string
  version: number
  status: FeatureTableStatus
  flowId: string | null
  flowDisplayName: string | null
  flowState: string | null
  archivedAt: string | null
  tableNames: string[]
  archivedTableNames: string[]
}

export function scanFeatureTables(): Promise<FeatureTableGroupDto[]> {
  return api<FeatureTableGroupDto[]>('/api/feature-tables')
}

export function archiveFeature(req: { flowCode: string; version: number; flowId?: string | null }): Promise<FeatureTableGroupDto> {
  return api<FeatureTableGroupDto>('/api/feature-tables/archive', { method: 'POST', json: req })
}

export function restoreFeature(req: { flowCode: string; version: number }): Promise<FeatureTableGroupDto> {
  return api<FeatureTableGroupDto>('/api/feature-tables/restore', { method: 'POST', json: req })
}
