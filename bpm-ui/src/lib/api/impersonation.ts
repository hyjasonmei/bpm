import { apiFetch } from '@/lib/apiFetch'
import type { ImpersonationSessionDto, StartImpersonationResult } from '@/types/impersonation'

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.title) detail = body.title
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.json() as T
}

export async function startImpersonation(targetUserId: string, reason: string): Promise<StartImpersonationResult> {
  const res = await apiFetch('/api/impersonation/start', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ targetUserId, reason }),
  })
  return jsonOrThrow<StartImpersonationResult>(res)
}

export async function endImpersonation(): Promise<void> {
  const res = await apiFetch('/api/impersonation/end', { method: 'POST' })
  if (!res.ok && res.status !== 204) {
    throw new Error(`end failed: ${res.status}`)
  }
}

export async function getImpersonationStatus(): Promise<ImpersonationSessionDto | null> {
  const res = await apiFetch('/api/impersonation/status')
  if (res.status === 204) return null
  if (!res.ok) return null
  const text = await res.text()
  return text ? JSON.parse(text) as ImpersonationSessionDto : null
}

export async function getImpersonationHistory(days = 30): Promise<ImpersonationSessionDto[]> {
  const res = await apiFetch(`/api/impersonation/sessions?days=${days}`)
  return jsonOrThrow<ImpersonationSessionDto[]>(res)
}
