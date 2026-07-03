/** Mirrors VENDOR_EXPENSE_V1_CaseStatus on the backend. */
export type VENDOR_EXPENSE_V1_Status =
  | 'PendingSupervisor'
  | 'PendingProcurement'
  | 'PendingSign'
  | 'ResubmitRequired'
  | 'Completed'
  | 'Cancelled'

export interface VENDOR_EXPENSE_V1_InvoiceDto {
  invoiceDate: string | null
  invoiceNo: string | null
  chargeTo: string | null
  project: string | null
  category: string | null
  amount: string | null
  currency: string | null
  description: string | null
}

export interface VENDOR_EXPENSE_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface VENDOR_EXPENSE_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  vendor: string | null
  submitterComment: string | null
  invoices: VENDOR_EXPENSE_V1_InvoiceDto[]
  status: VENDOR_EXPENSE_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  currentAssigneeRoleCode?: string | null
  supervisorDecision: VENDOR_EXPENSE_V1_DecisionDto | null
  procurementDecision: VENDOR_EXPENSE_V1_DecisionDto | null
  signDecision: VENDOR_EXPENSE_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
