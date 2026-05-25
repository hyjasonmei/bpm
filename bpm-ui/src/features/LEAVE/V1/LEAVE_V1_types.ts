/** Mirrors LEAVE_V1_CaseStatus on the backend. */
export type LEAVE_V1_Status =
  | 'PendingManager'
  | 'PendingVp'
  | 'PendingHr'
  | 'Completed'
  | 'Rejected'
  | 'Cancelled'

export interface LEAVE_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface LEAVE_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  leaveType: string
  startDate: string
  endDate: string
  days: number
  reason: string
  certFileId: string | null
  archiveNote: string | null
  status: LEAVE_V1_Status
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: LEAVE_V1_DecisionDto | null
  vpDecision: LEAVE_V1_DecisionDto | null
  hrArchive: LEAVE_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
