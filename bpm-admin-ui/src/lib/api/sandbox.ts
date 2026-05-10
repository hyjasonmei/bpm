import { apiFetch } from '@/lib/apiFetch'
import type { SandboxConfigDto, SandboxRedirectDto, SandboxStatusDto } from '@/types/sandbox'

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

export async function getSandboxStatus(): Promise<SandboxStatusDto> {
  const res = await apiFetch('/api/sandbox/status')
  return jsonOrThrow<SandboxStatusDto>(res)
}

export async function setSandboxStatus(enabled: boolean, config: SandboxConfigDto | null): Promise<SandboxStatusDto> {
  const res = await apiFetch('/api/sandbox/status', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled, config }),
  })
  return jsonOrThrow<SandboxStatusDto>(res)
}

export async function getSandboxRedirects(days = 30): Promise<SandboxRedirectDto[]> {
  const res = await apiFetch(`/api/sandbox/redirects?days=${days}`)
  return jsonOrThrow<SandboxRedirectDto[]>(res)
}
