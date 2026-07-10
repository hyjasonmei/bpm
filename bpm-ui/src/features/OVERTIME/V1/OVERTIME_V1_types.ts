/** Mirrors OVERTIME_V1_CaseStatus on the backend. */
export type OVERTIME_V1_Status =
  | 'PendingManager'
  | 'PendingHr'
  | 'Completed'
  | 'RejectedByManager'
  | 'RejectedByHr'
  | 'Cancelled'

export interface OVERTIME_V1_SubmitPayload {
  overtimeDate: string
  startTime: string
  endTime: string
  estimatedHours: string
  overtimeReason: string
}

export interface OVERTIME_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface OVERTIME_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  overtimeDate: string
  startTime: string | null
  endTime: string | null
  estimatedHours: number | null
  overtimeReason: string
  status: OVERTIME_V1_Status
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  currentAssigneeRoleCode: string | null
  monthlyHours: number | null
  managerDecision: OVERTIME_V1_DecisionDto | null
  hrDecision: OVERTIME_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
