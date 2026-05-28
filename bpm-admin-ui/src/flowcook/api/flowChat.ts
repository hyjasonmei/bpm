import { api } from '@/flowcook/api'

/** Mirrors Bpm.Admin.Application.Flows.FlowChatMessageDto. */
export interface FlowChatMessageDto {
  id: string
  flowId: string
  sender: 'User' | 'Chef' | 'System'
  kind: 'Memo' | 'Question' | 'Completion' | 'Reply' | 'Issue' | 'System'
  content: string
  artifactsJson: string | null
  version: string | null
  createdAt: string
  authorUserId: string | null
}

export function listFlowMessages(flowId: string, since?: string): Promise<FlowChatMessageDto[]> {
  const qs = since ? `?since=${encodeURIComponent(since)}` : ''
  return api<FlowChatMessageDto[]>(`/api/flows/${flowId}/messages${qs}`)
}

export function postChatReply(
  flowId: string,
  body: { kind: 'Reply' | 'Issue'; content: string },
): Promise<FlowChatMessageDto> {
  return api<FlowChatMessageDto>(`/api/flows/${flowId}/chat-reply`, { method: 'POST', json: body })
}

export function chefStallReset(flowId: string): Promise<unknown> {
  return api<unknown>(`/api/flows/${flowId}/chef-stall-reset`, { method: 'POST' })
}

// ── Simulate-chef admin demo helpers (user-JWT; same backend as real chef) ──

export function simulateChefStart(flowId: string, content?: string): Promise<FlowChatMessageDto> {
  return api<FlowChatMessageDto>(`/api/flows/${flowId}/simulate-chef/start`, {
    method: 'POST',
    json: { content },
  })
}

export function simulateChefAsk(flowId: string, question: string): Promise<FlowChatMessageDto> {
  return api<FlowChatMessageDto>(`/api/flows/${flowId}/simulate-chef/ask`, {
    method: 'POST',
    json: { question },
  })
}

export function simulateChefComplete(
  flowId: string,
  body: { content?: string; artifactsJson?: string; version?: string } = {},
): Promise<FlowChatMessageDto> {
  return api<FlowChatMessageDto>(`/api/flows/${flowId}/simulate-chef/complete`, {
    method: 'POST',
    json: body,
  })
}

export function simulateChefResume(flowId: string, content?: string): Promise<FlowChatMessageDto> {
  return api<FlowChatMessageDto>(`/api/flows/${flowId}/simulate-chef/resume`, {
    method: 'POST',
    json: { content },
  })
}
