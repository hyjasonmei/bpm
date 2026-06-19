/** Mirrors WFH_V6_CaseStatus on the backend. */
export type WFH_V6_Status =
  | 'PendingManager'
  | 'PendingSenior'
  | 'ResubmitRequired'
  | 'Completed'
  | 'Cancelled'

export interface WFH_V6_SubmitPayload {
  applyDate: string
  start: string
  end: string
  reason: string
  attachmentFileId: string | null
}

export interface WFH_V6_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface WFH_V6_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  applyDate: string
  startDate: string
  endDate: string
  days: number
  reason: string
  attachmentFileId: string | null
  status: WFH_V6_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: WFH_V6_DecisionDto | null
  seniorDecision: WFH_V6_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
