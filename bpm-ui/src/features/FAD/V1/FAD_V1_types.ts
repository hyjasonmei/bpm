export type FAD_V1_Status = 'PendingManager' | 'PendingConfirm' | 'ResubmitRequired' | 'Completed'

export interface FAD_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface FAD_V1_ConfirmDto {
  userId: string | null
  displayName: string | null
  handlingResult: string | null
  remark: string | null
  confirmedAt: string | null
}

export interface FAD_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  disposalReason: string
  assetId: string
  assetName: string
  description: string | null
  photoFileId: string | null
  status: FAD_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: FAD_V1_DecisionDto | null
  confirmation: FAD_V1_ConfirmDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
