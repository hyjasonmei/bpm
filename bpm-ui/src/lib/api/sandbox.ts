import { apiFetch } from '@/lib/apiFetch'
import type {
  SandboxStatusDto,
  SandboxClockDto,
  UnreadCountDto,
  SandboxPersonaDto,
  CapturedMessageSummaryDto,
  CapturedMessageDetailDto,
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

export async function getSandboxClock(): Promise<SandboxClockDto> {
  const res = await apiFetch('/api/sandbox/clock')
  return jsonOrThrow<SandboxClockDto>(res)
}

export async function getSandboxUnreadCount(): Promise<UnreadCountDto> {
  const res = await apiFetch('/api/sandbox/captured/unread-count')
  return jsonOrThrow<UnreadCountDto>(res)
}

export async function listSandboxPersonas(): Promise<SandboxPersonaDto[]> {
  const res = await apiFetch('/api/sandbox/personas')
  return jsonOrThrow<SandboxPersonaDto[]>(res)
}

export interface PersonaSwitchResponse {
  token: string
  expiresAt: string
  persona: { id: string; email: string; fullName: string; roles: string[] }
  actualActor: { id: string; email: string }
}

export async function switchSandboxPersona(userId: string): Promise<PersonaSwitchResponse> {
  const res = await apiFetch('/api/sandbox/persona', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId }),
  })
  return jsonOrThrow<PersonaSwitchResponse>(res)
}

/* ─── End-user mailbox (read-only subset) ─────────────────────────── */

export async function listSandboxCaptured(channel?: SandboxChannelValue): Promise<CapturedMessageSummaryDto[]> {
  const params = new URLSearchParams()
  if (channel != null) params.set('channel', channelToName(channel))
  params.set('limit', '50')
  const res = await apiFetch(`/api/sandbox/captured?${params}`)
  return jsonOrThrow<CapturedMessageSummaryDto[]>(res)
}

export async function getSandboxCaptured(id: string): Promise<CapturedMessageDetailDto> {
  const res = await apiFetch(`/api/sandbox/captured/${id}`)
  return jsonOrThrow<CapturedMessageDetailDto>(res)
}

function channelToName(c: SandboxChannelValue): string {
  switch (c) {
    case 1: return 'Email'
    case 2: return 'Webhook'
    case 3: return 'Sms'
    default: return 'Email'
  }
}
