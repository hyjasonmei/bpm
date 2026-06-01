export type FAP_V1_Status = 'PendingManager' | 'PendingVerification' | 'ResubmitRequired' | 'Completed'

export interface FAP_V1_PurchaseItemDto {
  category: string | null
  itemSpec: string | null
  qty: number
}

export interface FAP_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface FAP_V1_VerificationDto {
  userId: string | null
  displayName: string | null
  received: string | null
  remark: string | null
  verifiedAt: string | null
}

export interface FAP_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  purchaseItems: FAP_V1_PurchaseItemDto[]
  shippingLocation: string
  chargeTo: string
  purpose: string
  expectedDate: string | null
  note: string | null
  status: FAP_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: FAP_V1_DecisionDto | null
  purchaseOrderNo: string | null
  verification: FAP_V1_VerificationDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
