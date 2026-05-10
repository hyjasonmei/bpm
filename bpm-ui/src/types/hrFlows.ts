// Mirrors Bpm.Application.HrFlows.Dtos. Numeric enum values match the C#
// domain enums in Bpm.Domain.Entities.HrFlows.
//
// Sunset: when add-process-runtime ships, the backend endpoints become
// /api/process-instances and these types fold into ProcessInstanceDto etc.

export type HrFlowSpecCode = 'RESIGN' | 'DEPTX'

export const HrFlowStatus = {
  PendingManager: 1,
  PendingHr: 2,
  Returned: 3,
  Completed: 4,
  Cancelled: 5,
} as const
export type HrFlowStatusValue = typeof HrFlowStatus[keyof typeof HrFlowStatus]

export const HrFlowStep = {
  Apply: 1,
  ManagerApprove: 2,
  HrApprove: 3,
  Closed: 4,
} as const
export type HrFlowStepValue = typeof HrFlowStep[keyof typeof HrFlowStep]

export const HrFlowActionType = {
  Submit: 1,
  Approve: 2,
  Return: 3,
  Cancel: 4,
} as const
export type HrFlowActionTypeValue = typeof HrFlowActionType[keyof typeof HrFlowActionType]

// Server returns SpecCode as numeric (1=Resign, 2=Deptx) since C# is strongly
// typed. Convert at the api boundary.
export type HrFlowSpecCodeNumeric = 1 | 2

export interface HrFlowActionDto {
  id: string
  actorUserId: string
  actorName: string
  action: HrFlowActionTypeValue
  fromStep: HrFlowStepValue
  toStep: HrFlowStepValue
  comment: string | null
  createdAt: string
}

export interface HrFlowInstanceDto {
  id: string
  specCode: HrFlowSpecCodeNumeric
  initiatorUserId: string
  initiatorName: string
  resolvedManagerUserId: string
  managerName: string
  status: HrFlowStatusValue
  currentStep: HrFlowStepValue
  formData: Record<string, unknown>
  startedAt: string
  lastActivityAt: string
  completedAt: string | null
  cancelledAt: string | null
  actions: HrFlowActionDto[]
}

export interface HrFlowSummaryDto {
  id: string
  specCode: HrFlowSpecCodeNumeric
  initiatorUserId: string
  initiatorName: string
  status: HrFlowStatusValue
  currentStep: HrFlowStepValue
  startedAt: string
  lastActivityAt: string
}

export function specCodeToString(c: HrFlowSpecCodeNumeric): HrFlowSpecCode {
  return c === 1 ? 'RESIGN' : 'DEPTX'
}
