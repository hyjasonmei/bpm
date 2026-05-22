// Mirrors Bpm.Application.Attendance.Dtos.

export const PunchType = { In: 1, Out: 2 } as const
export type PunchTypeValue = typeof PunchType[keyof typeof PunchType]

export const PunchSource = { Manual: 1, Correction: 2, Auto: 3 } as const
export type PunchSourceValue = typeof PunchSource[keyof typeof PunchSource]

export const TodayState = { NotCheckedIn: 1, OnDuty: 2, OffDuty: 3 } as const
export type TodayStateValue = typeof TodayState[keyof typeof TodayState]

export interface PunchDto {
  id: string
  punchType: PunchTypeValue
  punchAt: string
  localDate: string
  source: PunchSourceValue
}

export interface TodayStatusDto {
  status: TodayStateValue
  workHours: number
  inProgress: boolean
  lastInAt: string | null
  lastOutAt: string | null
  punches: PunchDto[]
}

export interface DailySummaryDto {
  date: string
  firstIn: string | null
  lastOut: string | null
  workHours: number
  punchCount: number
}
