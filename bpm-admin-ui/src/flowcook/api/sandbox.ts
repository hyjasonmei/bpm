import { api } from '@/flowcook/api'

// All routed via the /bpmsvc dev proxy — the sandbox tables live on bpm-svc.
const base = '/bpmsvc/api/sandbox-admin'

export interface SandboxStatus {
  enabled: boolean
  config: unknown | null
  lastToggledAt: string | null
  lastToggledByUserId: string | null
}

export interface SandboxClock {
  realNow: string
  sandboxNow: string
  offsetSeconds: number
  sandboxOn: boolean
}

export interface SandboxPersona {
  id: string
  email: string
  fullName: string
  departmentName: string | null
}

export interface PersonaToken {
  token: string
  expiresAt: string
  persona: { id: string; email: string; fullName: string; roles: string[] }
}

export interface FlowSandboxState {
  flowCode: string
  displayName: string
  captureEnabled: boolean
}

export interface CapturedSummary {
  id: string
  processInstanceId: string | null
  taskId: string | null
  flowCode: string | null
  caseId: string | null
  channel: string
  subject: string | null
  eventType: string | null
  capturedAt: string
  readByMe: boolean
}

export interface CapturedDetail extends CapturedSummary {
  intendedRecipients: string[]
  bodyHtml: string | null
  bodyText: string | null
  url: string | null
  headersJson: string | null
  payloadJson: string | null
  body: string | null
  originatingNotificationId: string | null
  originatingWebhookSubscriptionId: string | null
}

export interface ResetSummary {
  instancesDeleted: number
  tasksDeleted: number
  historyRowsDeleted: number
  capturedMessagesDeleted: number
  casesDeleted: number
}

// ----- status -----
export const getSandboxStatus = () => api<SandboxStatus>(`${base}/status`)
export const setSandboxStatus = (enabled: boolean) =>
  api<SandboxStatus>(`${base}/status`, { method: 'PUT', json: { enabled } })

// ----- clock -----
export const getSandboxClock = () => api<SandboxClock>(`${base}/clock`)
export const advanceSandboxClock = (d: { days?: number; hours?: number; minutes?: number; seconds?: number }) =>
  api<SandboxClock>(`${base}/clock/advance`, { method: 'POST', json: d })
export const resetSandboxClock = () => api<SandboxClock>(`${base}/clock/reset`, { method: 'POST' })

// ----- persona -----
export const listSandboxPersonas = () => api<SandboxPersona[]>(`${base}/personas`)
export const mintPersonaToken = (userId: string) =>
  api<PersonaToken>(`${base}/persona`, { method: 'POST', json: { userId } })

// ----- mailbox -----
export function listCaptured(q: { flowCode?: string; channel?: string; unread?: boolean } = {}) {
  const p = new URLSearchParams()
  if (q.flowCode) p.set('flowCode', q.flowCode)
  if (q.channel) p.set('channel', q.channel)
  if (q.unread) p.set('unread', 'true')
  const qs = p.toString()
  return api<CapturedSummary[]>(`${base}/captured${qs ? `?${qs}` : ''}`)
}
export const getCaptured = (id: string) => api<CapturedDetail>(`${base}/captured/${id}`)

// ----- per-flow scope -----
export const listFlowSandbox = () => api<FlowSandboxState[]>(`${base}/flows`)
export const setFlowCapture = (flowCode: string, enabled: boolean) =>
  api<FlowSandboxState>(`${base}/flows/${flowCode}`, { method: 'PUT', json: { enabled } })

// ----- reset -----
export const resetAll = () => api<ResetSummary>(`${base}/reset/all`, { method: 'POST' })
export const resetFlow = (flowCode: string) =>
  api<ResetSummary>(`${base}/reset/flow/${flowCode}`, { method: 'POST' })
