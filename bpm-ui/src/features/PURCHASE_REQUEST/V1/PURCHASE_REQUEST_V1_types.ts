/** Mirrors PURCHASE_REQUEST_V1_CaseStatus on the backend. */
export type PURCHASE_REQUEST_V1_Status =
  | 'PendingDeptHead'
  | 'PendingFinance'
  | 'ResubmitRequired'
  | 'Completed'
  | 'Cancelled'

export interface PURCHASE_REQUEST_V1_InvoiceDto {
  invoiceDate: string
  invoiceNo: string | null
  chargeTo: string | null
  project: string | null
  category: string | null
  amount: string | null
  currency: string | null
  attachmentFileId: string | null
  description: string | null
}

export interface PURCHASE_REQUEST_V1_DecisionDto {
  userId: string | null
  displayName: string | null
  approved: boolean | null
  comment: string | null
  decidedAt: string | null
}

export interface PURCHASE_REQUEST_V1_CaseResponse {
  id: string
  submitterUserId: string
  submitterDisplayName: string | null
  submitterComment: string
  invoices: PURCHASE_REQUEST_V1_InvoiceDto[]
  status: PURCHASE_REQUEST_V1_Status
  roundCount: number
  currentAssigneeUserId: string | null
  currentAssigneeDisplayName: string | null
  currentAssigneeRoleCode?: string | null
  deptHeadDecision: PURCHASE_REQUEST_V1_DecisionDto | null
  financeDecision: PURCHASE_REQUEST_V1_DecisionDto | null
  submittedAt: string
  lastActivityAt: string
  completedAt: string | null
}
