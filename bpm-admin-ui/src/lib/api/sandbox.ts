import { apiFetch } from '@/lib/apiFetch'
import type {
  SandboxConfigDto,
  SandboxRedirectDto,
  SandboxStatusDto,
  CapturedMessageSummaryDto,
  CapturedMessageDetailDto,
  UnreadCountDto,
  SandboxClockDto,
  SandboxPersonaDto,
  SandboxChannelValue,
} from '@/types/sandbox'

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.error) detail = body.error
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

/* ─── PR-J5: Mailbox / Clock / Personas ─────────────────────────── */

export interface ListCapturedFilters {
  channel?: SandboxChannelValue | null
  recipientUserId?: string | null
  processInstanceId?: string | null
  unread?: boolean
  limit?: number
}

export async function listCaptured(f: ListCapturedFilters = {}): Promise<CapturedMessageSummaryDto[]> {
  const params = new URLSearchParams()
  if (f.channel != null) params.set('channel', channelToName(f.channel))
  if (f.recipientUserId) params.set('recipientUserId', f.recipientUserId)
  if (f.processInstanceId) params.set('processInstanceId', f.processInstanceId)
  if (f.unread) params.set('unread', 'true')
  params.set('limit', String(f.limit ?? 50))
  const res = await apiFetch(`/api/sandbox/captured?${params}`)
  return jsonOrThrow<CapturedMessageSummaryDto[]>(res)
}

export async function getCaptured(id: string): Promise<CapturedMessageDetailDto> {
  const res = await apiFetch(`/api/sandbox/captured/${id}`)
  return jsonOrThrow<CapturedMessageDetailDto>(res)
}

export async function markCapturedRead(id: string): Promise<void> {
  const res = await apiFetch(`/api/sandbox/captured/${id}/read`, { method: 'POST' })
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try { const b = await res.json(); detail = b?.error ?? b?.detail ?? detail } catch { /* ignore */ }
    throw new Error(detail)
  }
}

export async function getUnreadCount(): Promise<UnreadCountDto> {
  const res = await apiFetch('/api/sandbox/captured/unread-count')
  return jsonOrThrow<UnreadCountDto>(res)
}

export async function getClock(): Promise<SandboxClockDto> {
  const res = await apiFetch('/api/sandbox/clock')
  return jsonOrThrow<SandboxClockDto>(res)
}

export async function advanceClock(parts: { days?: number; hours?: number; minutes?: number; seconds?: number }): Promise<SandboxClockDto> {
  const res = await apiFetch('/api/sandbox/clock/advance', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      days:    parts.days    ?? 0,
      hours:   parts.hours   ?? 0,
      minutes: parts.minutes ?? 0,
      seconds: parts.seconds ?? 0,
    }),
  })
  return jsonOrThrow<SandboxClockDto>(res)
}

export async function resetClock(): Promise<SandboxClockDto> {
  const res = await apiFetch('/api/sandbox/clock/reset', { method: 'POST' })
  return jsonOrThrow<SandboxClockDto>(res)
}

export async function listSandboxPersonas(): Promise<SandboxPersonaDto[]> {
  const res = await apiFetch('/api/sandbox/personas')
  return jsonOrThrow<SandboxPersonaDto[]>(res)
}

export async function switchSandboxPersona(userId: string): Promise<{ token: string; expiresAt: string; persona: { id: string; email: string; fullName: string; roles: string[] }; actualActor: { id: string; email: string } }> {
  const res = await apiFetch('/api/sandbox/persona', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId }),
  })
  return jsonOrThrow(res)
}

function channelToName(c: SandboxChannelValue): string {
  switch (c) {
    case 1: return 'Email'
    case 2: return 'Webhook'
    case 3: return 'Sms'
    default: return 'Email'
  }
}
