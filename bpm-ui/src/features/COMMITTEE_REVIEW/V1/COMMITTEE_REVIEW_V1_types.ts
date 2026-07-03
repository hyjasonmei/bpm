export type CmSlotState = 'pending' | 'approved' | 'rejected' | 'skipped'

export interface CmSlotView {
  slotId: string
  nodeId: string
  roleCode?: string
  state: CmSlotState
  deciderName?: string
  comment?: string
  at?: string
}

export interface CmReviewView {
  policyLabel: string
  threshold: number
  approvedCount: number
  total: number
  slots: CmSlotView[]
}

export interface CmCaseResponse {
  id: string
  title: string
  amount: number
  currency: string
  purpose: string
  status: string
  submitterUserId: string
  submitterName?: string
  submittedAt: string
  lastActivityAt: string
  review?: CmReviewView
}
