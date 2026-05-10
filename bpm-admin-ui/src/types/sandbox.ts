export interface SandboxConfigDto {
  emailRecipients: string[] | null
  webhookUrl: string | null
  smsRecipients: string[] | null
}

export interface SandboxStatusDto {
  enabled: boolean
  config: SandboxConfigDto | null
  lastToggledAt: string | null
  lastToggledByUserId: string | null
}

export const SandboxChannel = { Email: 1, Webhook: 2, Sms: 3 } as const
export type SandboxChannelValue = typeof SandboxChannel[keyof typeof SandboxChannel]

export const SandboxAction = { Redirected: 1, Dropped: 2 } as const
export type SandboxActionValue = typeof SandboxAction[keyof typeof SandboxAction]

export interface SandboxRedirectDto {
  id: string
  channel: SandboxChannelValue
  action: SandboxActionValue
  originalTargets: string[]
  redirectedTargets: string[]
  sampleSubject: string | null
  dispatchedAt: string
}
