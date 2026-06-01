export type TEO_V1_Status = 'PendingManager' | 'PendingFinance' | 'ResubmitRequired' | 'Completed'

export interface TEO_V1_ExpenseItemDto {
  date: string
  country: string | null
  category: string | null
  description: string | null
  amount: string | null
  amountLcy: string | null
}

export interface TEO_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface TEO_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  travelRequestNo: string
  expenseItems: TEO_V1_ExpenseItemDto[]
  status: TEO_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  managerDecision: TEO_V1_DecisionDto | null
  financeDecision: TEO_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
