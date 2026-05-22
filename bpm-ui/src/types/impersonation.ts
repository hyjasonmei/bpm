export interface StartImpersonationResult {
  token: string
  expiresAt: string
  sessionId: string
  targetUserId: string
  targetFullName: string
}

export const EndReason = { ManualExit: 1, AutoExpired: 2, AdminRevoked: 3 } as const
export type EndReasonValue = typeof EndReason[keyof typeof EndReason]

export interface ImpersonationSessionDto {
  id: string
  impersonatorUserId: string
  impersonatorName: string
  targetUserId: string
  targetName: string
  startedAt: string
  endedAt: string | null
  endReason: EndReasonValue | null
  reason: string
}

export interface UserSummaryForPicker {
  id: string
  fullName: string
  email: string
}
