// Mirrors TravelCaseDto in bpm-svc/src/Application/Travel/Dtos/TravelCaseDto.cs
export type TravelState = 0 | 1 | 2 | 3 | 4 | 5

export const TRAVEL_STATE_LABEL: Record<TravelState, { en: string; zh: string }> = {
  0: { en: 'Draft', zh: '草稿' },
  1: { en: 'Pending manager approval', zh: '待主管核准' },
  2: { en: 'Pending VP approval', zh: '待副總核准' },
  3: { en: 'Pending admin book', zh: '待行政訂票' },
  4: { en: 'Completed', zh: '已完成' },
  5: { en: 'Rejected', zh: '已退回' },
}

export interface TravelCaseDto {
  id: string
  tenantCode: string
  flowCode: string
  state: TravelState
  applicantUserId: string
  destinationType: string
  destination: string
  departDate: string
  returnDate: string
  purpose: string
  estimatedCost: number
  ticketRef: string | null
  hotelRef: string | null
  bookNote: string | null
  currentApproverUserId: string | null
  managerApproverUserId: string | null
  managerApprovedAt: string | null
  vpApproverUserId: string | null
  vpApprovedAt: string | null
  adminBookerUserId: string | null
  adminBookedAt: string | null
  rejectedByUserId: string | null
  rejectedAt: string | null
  rejectionReason: string | null
  createdAt: string
  updatedAt: string
  createdBy: string
}

export interface ApiError { status: number; title: string; detail?: string; errors?: Record<string, string[]> }

async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) {
    let msg: ApiError = { status: res.status, title: res.statusText }
    try { msg = { ...msg, ...(await res.json()) } } catch { /* ignore */ }
    throw msg
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const travelApi = {
  submit: (input: {
    tenantCode: string
    applicantUserId: string
    destinationType: string
    destination: string
    departDate: string
    returnDate: string
    purpose: string
    estimatedCost: number
  }) => send<TravelCaseDto>('POST', '/api/travel/cases', input),

  get: (id: string) => send<TravelCaseDto>('GET', `/api/travel/cases/${id}`),

  list: (q: { applicantUserId?: string; currentApproverUserId?: string }) => {
    const params = new URLSearchParams()
    if (q.applicantUserId) params.set('applicantUserId', q.applicantUserId)
    if (q.currentApproverUserId) params.set('currentApproverUserId', q.currentApproverUserId)
    const qs = params.toString()
    return send<TravelCaseDto[]>('GET', `/api/travel/cases${qs ? '?' + qs : ''}`)
  },

  approve: (id: string, approverUserId: string) =>
    send<TravelCaseDto>('POST', `/api/travel/cases/${id}/approve`, { approverUserId }),

  reject: (id: string, approverUserId: string, reason: string) =>
    send<TravelCaseDto>('POST', `/api/travel/cases/${id}/reject`, { approverUserId, reason }),

  book: (id: string, body: { adminUserId: string; ticketRef: string; hotelRef: string | null; bookNote: string | null }) =>
    send<TravelCaseDto>('POST', `/api/travel/cases/${id}/book`, body),
}

// Persona → spec.testCases-aligned employee id (mirrors identity-acme.csv).
export function personaToSpecUserId(persona: string): string | null {
  switch (persona) {
    case 'employee': return 'u_wilson'
    case 'manager':  return 'u_wang_manager'
    case 'admin':    return 'u_admin_lead'
    default:         return null
  }
}

// During state=PendingVpApproval, the manager persona substitutes u_chen_vp
// (mirrors LEAVE_SPEC's manager → u_chen_vp trick for VP escalation).
export function personaToActingUserId(persona: string, state: TravelState): string | null {
  if (persona === 'manager' && state === 2) return 'u_chen_vp'
  return personaToSpecUserId(persona)
}

export function specUserIdToLabel(id: string | null | undefined): string {
  if (!id) return '—'
  return ({
    u_wilson:        'Wilson Liu (員工)',
    u_mary:          'Mary Chen (員工)',
    u_wang_manager:  'Wang Manager (主管)',
    u_chen_vp:       'Chen VP (副總)',
    u_admin_lead:    'Anna Admin (行政)',
  } as Record<string, string>)[id] ?? id
}
