import { api } from '@/flowcook/api'

const base = '/bpmsvc/api/support/issues'

// Mirrors Bpm.Domain.Entities.Support.SupportIssueStatus.
export const IssueStatus = { New: 1, Acknowledged: 2, Closed: 3 } as const
export type IssueStatusValue = typeof IssueStatus[keyof typeof IssueStatus]

export interface IssueDto {
  id: string
  userId: string
  userName: string
  kind: string          // bug | feature | question
  title: string
  description: string
  contact: string | null
  page: string | null
  userAgent: string | null
  status: IssueStatusValue
  submittedAt: string
}

export async function listIssues(status?: IssueStatusValue): Promise<IssueDto[]> {
  const q = status != null ? `?status=${status}` : ''
  return api<IssueDto[]>(`${base}${q}`)
}

export async function setIssueStatus(id: string, status: IssueStatusValue): Promise<IssueDto> {
  return api<IssueDto>(`${base}/${id}`, { method: 'PATCH', json: { status } })
}
