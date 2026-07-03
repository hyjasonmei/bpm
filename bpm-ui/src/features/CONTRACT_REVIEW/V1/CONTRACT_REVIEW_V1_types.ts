export type CrSlotState = 'pending' | 'approved' | 'rejected' | 'skipped'

export interface CrSlotView {
  slotId: string
  nodeId: string
  roleCode?: string
  state: CrSlotState
  deciderName?: string
  comment?: string
  at?: string
}

export interface CrReviewView {
  policyLabel: string
  threshold: number
  approvedCount: number
  total: number
  slots: CrSlotView[]
}

export interface CrCaseResponse {
  id: string
  title: string
  counterparty: string
  amount: number
  currency: string
  status: string
  submitterUserId: string
  submitterName?: string
  submittedAt: string
  lastActivityAt: string
  review?: CrReviewView
}
