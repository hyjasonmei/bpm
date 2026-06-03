/** Mirrors APE_V1_CaseStatus on the backend. */
export type APE_V1_Status = 'PendingManager' | 'ResubmitRequired' | 'Completed' | 'Cancelled'

export interface APE_V1_SubmitPayload {
  expectReceiveDate: string
  deductReturnDate: string
  chargeDepartment: string
  rechargeOutside: string | null
  description: string
  amount: string
  currency: string
}

export interface APE_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface APE_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  expectReceiveDate: string
  deductReturnDate: string
  chargeDepartment: string
  rechargeOutside: string | null
  description: string
  amount: number
  currency: string
  status: APE_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: APE_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
