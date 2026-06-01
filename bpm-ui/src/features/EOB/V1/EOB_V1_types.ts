export type EOB_V1_Status = 'PendingManager' | 'PendingSetup' | 'ResubmitRequired' | 'Completed'

export interface EOB_V1_SetupTaskDto {
  task: string | null
  status: string | null
}

export interface EOB_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface EOB_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  firstName: string
  lastName: string
  businessTitle: string
  employeeLocation: string
  onboardDate: string
  requireMailbox: string | null
  costCenter: string
  contractNumber: string | null
  contractEffectiveDate: string | null
  contractExpirationDate: string | null
  setupTasks: EOB_V1_SetupTaskDto[]
  status: EOB_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: EOB_V1_DecisionDto | null
  setupByUserId: string | null
  setupByDisplayName: string | null
  setupAt: string | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
