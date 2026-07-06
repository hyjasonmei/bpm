export type PrincipalType = 0 | 1 | 2 // User | Dept | Group

export interface Principal {
  id: string
  type: PrincipalType
  displayName: string
  email: string | null
  active: boolean
  createdAt: string
  updatedAt: string
}

export interface Role {
  id: string
  code: string
  name: string
  isSystem: boolean
  description: string | null
}

export interface PrincipalRole {
  principalId: string
  roleId: string
  inheritToMembers: boolean
  /** Dept principals only: the role also reaches every descendant dept's members. */
  includeSubDepts: boolean
  assignedAt: string
  assignedByUserId: string | null
}

export interface Delegation {
  id: string
  delegatorPrincipalId: string
  delegateToUserId: string
  startAt: string
  endAt: string
  active: boolean
  reason: string | null
}

export interface EffectiveRole {
  roleId: string
  sourcePrincipalId: string
  viaInherit: boolean
}

export interface CurrentUser {
  userId: string
  displayName: string
  email: string | null
}

export const principalTypeLabel = (t: PrincipalType): string => {
  switch (t) {
    case 0:
      return 'User'
    case 1:
      return 'Dept'
    case 2:
      return 'Group'
  }
}
