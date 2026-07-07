import { apiFetch } from '@/lib/apiFetch'

export interface SubmitIssueRequest {
  kind: 'bug' | 'feature' | 'question'
  title: string
  description: string
  contact: string | null
  page: string | null
}

export interface IssueDto {
  id: string
  kind: string
  title: string
  status: number
  submittedAt: string
}

export async function submitIssue(req: SubmitIssueRequest): Promise<IssueDto> {
  const res = await apiFetch('/api/support/issues', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  })
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.title) detail = body.title
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.json() as IssueDto
}
