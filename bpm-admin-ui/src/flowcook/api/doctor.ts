import { api } from '@/flowcook/api'

const base = '/bpmsvc/api/doctor'

export interface CaseFinding {
  rule: 'resigned_approver' | 'ownerless' | 'stalled'
  severity: 'high' | 'medium' | 'info'
  flowCode: string
  caseId: string
  statusName: string | null
  assigneeUserId: string | null
  assigneeName: string | null
  assigneeGone: boolean
  submitterUserId: string | null
  submitterName: string | null
  submittedAt: string
  lastActivityAt: string
  daysStuck: number
  suggestedUserId: string | null
  suggestedName: string | null
  suggestedVia: string | null
}

export interface OrgFinding {
  rule: 'no_dept_head' | 'empty_role' | 'empty_group'
  severity: string
  kind: 'user' | 'dept' | 'role' | 'group'
  principalId: string | null
  name: string
  detail: string
}

export interface DepartedPerson {
  userId: string
  name: string
  active: boolean
  deleted: boolean
  openCaseCount: number
}

export interface DoctorReport {
  caseFindings: CaseFinding[]
  orgFindings: OrgFinding[]
  departedWithCases: DepartedPerson[]
}

export interface DoctorCandidate {
  userId: string
  name: string
  email: string | null
  hint: string | null
}

export interface DoctorCandidates {
  suggested: DoctorCandidate | null
  users: DoctorCandidate[]
}

export interface DoctorActionResult {
  ok: boolean
  affected: number
  error: string | null
}

export const scanDoctor = (stalledDays = 14) =>
  api<DoctorReport>(`${base}/scan?stalledDays=${stalledDays}`)

export const getCandidates = (userId?: string) =>
  api<DoctorCandidates>(`${base}/candidates${userId ? `?userId=${userId}` : ''}`)

export const reassignCase = (flowCode: string, caseId: string, toUserId: string, reason?: string) =>
  api<DoctorActionResult>(`${base}/reassign`, { method: 'POST', json: { flowCode, caseId, toUserId, reason } })

export const batchReassign = (fromUserId: string, toUserId: string, reason?: string) =>
  api<DoctorActionResult>(`${base}/batch-reassign`, { method: 'POST', json: { fromUserId, toUserId, reason } })

export const cancelCase = (flowCode: string, caseId: string, reason?: string) =>
  api<DoctorActionResult>(`${base}/cancel`, { method: 'POST', json: { flowCode, caseId, reason } })
