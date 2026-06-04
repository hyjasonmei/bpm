import { api } from '@/flowcook/api'

export interface AuditEvent {
  eventId: string
  timestamp: string
  actionType: string
  targetType: string
  targetId: string | null
  actorUserId: string | null
  actorDisplayName: string | null
  sourceSystem: string
  reason: string | null
  beforeJson: string | null
  afterJson: string | null
}

export interface AuditPage {
  items: AuditEvent[]
  total: number
}

export interface AuditFacets {
  actionTypes: string[]
  targetTypes: string[]
  sources: string[]
}

export interface AuditQuery {
  actionType?: string
  targetType?: string
  source?: string
  actorUserId?: string
  search?: string
  from?: string
  to?: string
  skip?: number
  take?: number
}

export function listAuditEvents(q: AuditQuery = {}): Promise<AuditPage> {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(q)) {
    if (v !== undefined && v !== null && v !== '') p.set(k, String(v))
  }
  const qs = p.toString()
  return api<AuditPage>(`/api/audit-events${qs ? `?${qs}` : ''}`)
}

export const getAuditFacets = () => api<AuditFacets>('/api/audit-events/facets')
