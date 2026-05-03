// Mirrors PurchaseCaseDto in bpm-svc/src/Application/Purchase/Dtos/PurchaseCaseDto.cs
export type PurchaseState =
  | 0   // Draft
  | 1   // PendingManagerApproval
  | 2   // PendingFinanceApproval
  | 3   // PendingCeoApproval
  | 4   // PendingPurchaseExec
  | 5   // Completed
  | 6   // Rejected

export const PURCHASE_STATE_LABEL: Record<PurchaseState, { en: string; zh: string }> = {
  0: { en: 'Draft', zh: '草稿' },
  1: { en: 'Pending manager approval', zh: '待主管核准' },
  2: { en: 'Pending finance approval', zh: '待財務核准' },
  3: { en: 'Pending CEO approval', zh: '待 CEO 核准' },
  4: { en: 'Pending purchase exec', zh: '待採購處理' },
  5: { en: 'Completed', zh: '已完成' },
  6: { en: 'Rejected', zh: '已退回' },
}

export interface PurchaseCaseDto {
  id: string
  tenantCode: string
  flowCode: string
  state: PurchaseState
  applicantUserId: string
  vendor: string
  category: string
  amount: number
  items: string
  justification: string
  quoteFileName: string | null
  poNumber: string | null
  expectedDelivery: string | null
  execNote: string | null
  currentApproverUserId: string | null
  managerApproverUserId: string | null
  managerApprovedAt: string | null
  financeApproverUserId: string | null
  financeApprovedAt: string | null
  ceoApproverUserId: string | null
  ceoApprovedAt: string | null
  purchaseExecUserId: string | null
  purchaseExecAt: string | null
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

export const purchaseApi = {
  submit: (input: {
    tenantCode: string
    applicantUserId: string
    vendor: string
    category: string
    amount: number
    items: string
    justification: string
    quoteFileName: string | null
  }) => send<PurchaseCaseDto>('POST', '/api/purchase/cases', input),

  get: (id: string) => send<PurchaseCaseDto>('GET', `/api/purchase/cases/${id}`),

  list: (q: { applicantUserId?: string; currentApproverUserId?: string }) => {
    const params = new URLSearchParams()
    if (q.applicantUserId) params.set('applicantUserId', q.applicantUserId)
    if (q.currentApproverUserId) params.set('currentApproverUserId', q.currentApproverUserId)
    const qs = params.toString()
    return send<PurchaseCaseDto[]>('GET', `/api/purchase/cases${qs ? '?' + qs : ''}`)
  },

  approve: (id: string, approverUserId: string) =>
    send<PurchaseCaseDto>('POST', `/api/purchase/cases/${id}/approve`, { approverUserId }),

  reject: (id: string, approverUserId: string, reason: string) =>
    send<PurchaseCaseDto>('POST', `/api/purchase/cases/${id}/reject`, { approverUserId, reason }),

  execute: (id: string, body: { execUserId: string; poNumber: string; expectedDelivery: string; execNote: string | null }) =>
    send<PurchaseCaseDto>('POST', `/api/purchase/cases/${id}/execute`, body),
}

// Persona → spec.testCases-aligned employee id (mirrors identity-acme.csv).
export function personaToSpecUserId(persona: string): string | null {
  switch (persona) {
    case 'employee': return 'u_wilson'
    case 'manager':  return 'u_wang_manager'
    case 'finance':  return 'u_finance_lead'
    case 'admin':    return 'u_purchase_lead'
    default:         return null
  }
}

// During state=PendingCeoApproval, the finance persona substitutes u_ceo
// (mirrors LEAVE_SPEC's manager → u_chen_vp trick for VP escalation).
export function personaToActingUserId(persona: string, state: PurchaseState): string | null {
  if (persona === 'finance' && state === 3) return 'u_ceo'
  return personaToSpecUserId(persona)
}

export function specUserIdToLabel(id: string | null | undefined): string {
  if (!id) return '—'
  return ({
    u_wilson:        'Wilson Liu (員工)',
    u_mary:          'Mary Chen (員工)',
    u_wang_manager:  'Wang Manager (主管)',
    u_chen_vp:       'Chen VP (副總)',
    u_finance_lead:  'Lin Finance (財務)',
    u_purchase_lead: 'Sam Purchasing (採購)',
    u_ceo:           'Anna CEO',
  } as Record<string, string>)[id] ?? id
}
