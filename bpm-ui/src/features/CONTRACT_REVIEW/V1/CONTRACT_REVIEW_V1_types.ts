import type { FilePickerValue } from '@/components/ui/FilePicker'

export type CrStatus =
  | 'PendingParallelReview'
  | 'ResubmitRequired'
  | 'PendingLegalManager'
  | 'Completed'
  | 'Cancelled'

export type CrSlotState = 'pending' | 'approved' | 'rejected' | 'skipped'

export interface CrSlotView {
  slotId: string
  nodeId: string
  roleCode?: string | null
  state: CrSlotState
  deciderName?: string | null
  comment?: string | null
  at?: string | null
}

export interface CrReviewView {
  policyLabel: string
  threshold: number
  approvedCount: number
  total: number
  slots: CrSlotView[]
}

export interface CrLegalManagerView {
  userId?: string | null
  name?: string | null
  approved?: boolean | null
  comment?: string | null
  at?: string | null
}

export interface CrCaseResponse {
  id: string
  counterpartyName: string
  contractSubject: string
  amount: number
  periodStart: string
  periodEnd: string
  draftFileId?: string | null
  remarks?: string | null
  revisionNote?: string | null
  status: CrStatus
  currentRound: number
  submitterUserId: string
  submitterName?: string | null
  submittedAt: string
  lastActivityAt: string
  completedAt?: string | null
  review?: CrReviewView | null
  legalManager?: CrLegalManagerView | null
}

/** Local form model (create + revise share the same fields; revise adds a note). */
export interface CrFormState {
  counterpartyName: string
  contractSubject: string
  amount: string
  periodStart: string
  periodEnd: string
  draftFile: FilePickerValue | null
  remarks: string
  revisionNote: string
}

export function emptyForm(): CrFormState {
  return {
    counterpartyName: '',
    contractSubject: '',
    amount: '',
    periodStart: '',
    periodEnd: '',
    draftFile: null,
    remarks: '',
    revisionNote: '',
  }
}
