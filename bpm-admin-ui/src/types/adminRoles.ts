export const RoleScope = { System: 1, Tenant: 2 } as const
export type RoleScopeValue = typeof RoleScope[keyof typeof RoleScope]

export const AssignmentScope = { Tenant: 1, Flow: 2 } as const
export type AssignmentScopeValue = typeof AssignmentScope[keyof typeof AssignmentScope]

export interface RoleSummaryDto {
  id: string
  code: string
  name: string
  scope: RoleScopeValue
  assignedCount: number
}

export interface UserSummaryDto {
  id: string
  fullName: string
  email: string
  departmentCode: string | null
  isActive: boolean
  roleCount: number
}

export interface AssignmentDto {
  id: string
  roleId: string
  roleCode: string
  roleName: string
  scope: AssignmentScopeValue
  scopeRef: string | null
  assignedAt: string
  assignedBy: string | null
}

export interface UserDetailDto {
  profile: UserSummaryDto
  assignments: AssignmentDto[]
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}
