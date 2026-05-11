export interface SandboxConfigDto {
  emailRecipients: string[] | null
  webhookUrl: string | null
  smsRecipients: string[] | null
  captureRetentionDays?: number
}

export interface SandboxStatusDto {
  enabled: boolean
  config: SandboxConfigDto | null
  lastToggledAt: string | null
  lastToggledByUserId: string | null
}

export const SandboxChannel = { Email: 1, Webhook: 2, Sms: 3 } as const
export type SandboxChannelValue = typeof SandboxChannel[keyof typeof SandboxChannel]

/* ─── PR-J5 ───────────────────────────────────────────── */

export interface CapturedMessageSummaryDto {
  id: string
  processInstanceId: string | null
  taskId: string | null
  channel: SandboxChannelValue
  subject: string | null
  eventType: string | null
  capturedAt: string
  readByMe: boolean
}

export interface CapturedMessageDetailDto {
  id: string
  processInstanceId: string | null
  taskId: string | null
  channel: SandboxChannelValue
  intendedRecipients: string[]
  subject: string | null
  bodyHtml: string | null
  bodyText: string | null
  url: string | null
  headersJson: string | null
  payloadJson: string | null
  eventType: string | null
  body: string | null
  capturedAt: string
  readByMe: boolean
  originatingNotificationId: string | null
  originatingWebhookSubscriptionId: string | null
}

export interface UnreadCountDto {
  total: number
  byChannel: Record<string, number>
}

export interface SandboxClockDto {
  realNow: string
  sandboxNow: string
  offsetSeconds: number
  sandboxOn: boolean
}

export interface SandboxPersonaDto {
  id: string
  email: string
  fullName: string
  departmentName: string | null
}
