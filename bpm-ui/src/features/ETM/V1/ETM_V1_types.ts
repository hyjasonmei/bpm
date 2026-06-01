export type ETM_V1_Status = 'PendingManager' | 'PendingHandover' | 'ResubmitRequired' | 'Completed'

export interface ETM_V1_ReturnItemDto {
  assetType: string | null
  action: string | null
  status: string | null
}

export interface ETM_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface ETM_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  employeeName: string
  employeeId: string
  lastWorkingDate: string
  reason: string
  provideCertificate: string | null
  outstandingPayment: string | null
  returnItems: ETM_V1_ReturnItemDto[]
  status: ETM_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: ETM_V1_DecisionDto | null
  handoverByUserId: string | null
  handoverByDisplayName: string | null
  handoverAt: string | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
