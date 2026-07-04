import type { FilePickerValue } from '@/components/ui/FilePicker'

export type CmStatus =
  | 'PendingParallelReview'
  | 'ResubmitRequired'
  | 'PendingCeo'
  | 'Completed'
  | 'Rejected'
  | 'Cancelled'

export type CmSlotState = 'pending' | 'approved' | 'rejected' | 'skipped'

export interface CmSlotView {
  slotId: string
  nodeId: string
  roleCode?: string | null
  state: CmSlotState
  deciderName?: string | null
  comment?: string | null
  at?: string | null
}

export interface CmReviewView {
  policyLabel: string
  threshold: number
  approvedCount: number
  total: number
  slots: CmSlotView[]
}

export interface CmCeoView {
  userId?: string | null
  name?: string | null
  approved?: boolean | null
  comment?: string | null
  at?: string | null
}

export interface CmCaseResponse {
  id: string
  caseTitle: string
  reviewCategory: string
  reviewCategoryLabel: string
  applyAmount: number
  benefitDescription: string
  execStart: string
  execEnd: string
  attachmentFileId?: string | null
  remarks?: string | null
  revisionNote?: string | null
  status: CmStatus
  currentRound: number
  submitterUserId: string
  submitterName?: string | null
  submittedAt: string
  lastActivityAt: string
  completedAt?: string | null
  review?: CmReviewView | null
  ceo?: CmCeoView | null
}

/** review_category options (spec userTasks[task_apply].fields). */
export const CATEGORY_OPTIONS: { value: string; label: string }[] = [
  { value: 'major_procurement', label: '重大採購' },
  { value: 'capital_expenditure', label: '資本支出' },
  { value: 'other', label: '其他' },
]

/** Local form model (create + revise share the same fields; revise adds a note). */
export interface CmFormState {
  caseTitle: string
  reviewCategory: string
  applyAmount: string
  benefitDescription: string
  execStart: string
  execEnd: string
  attachment: FilePickerValue | null
  remarks: string
  revisionNote: string
}

export function emptyForm(): CmFormState {
  return {
    caseTitle: '',
    reviewCategory: '',
    applyAmount: '',
    benefitDescription: '',
    execStart: '',
    execEnd: '',
    attachment: null,
    remarks: '',
    revisionNote: '',
  }
}
