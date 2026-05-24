/**
 * Bundle DTOs kept after the legacy admin retirement. Only the shapes
 * still referenced by the V0 AI Kitchen onboarding wizard (which
 * builds + hydrates spec bundles in-memory) live here. The legacy
 * Flow Library list / detail / import / repro types were retired
 * along with their UI.
 */

export interface BundleFileEntry {
  path: string
  sha256: string
  sizeBytes: number
  kind: string
}

export interface BundleManifest {
  bundleSchemaVersion: number
  flowCode: string
  flowVersion: number
  exportedAt: string
  sourceInstanceId: string
  parent: string | null
  files: BundleFileEntry[]
}

export interface UserSnapshot {
  id: string
  email: string
  fullName: string
  managerId: string | null
  departmentId: string | null
}

export interface DepartmentSnapshot {
  id: string
  code: string
  name: string
  parentId: string | null
  headUserId: string | null
}

export interface GroupSnapshot {
  id: string
  code: string
  name: string
  memberPrincipalIds: string[]
}

export interface RoleAssignmentSnapshot {
  roleCode: string
  principalId: string
  scope: string
  scopeRef: string | null
}

export interface SampleOrgSnapshot {
  users: UserSnapshot[]
  departments: DepartmentSnapshot[]
  groups: GroupSnapshot[]
  roleAssignments: RoleAssignmentSnapshot[]
}

export interface TestCaseSnapshot {
  id: string
  name: string
  inputs: unknown
  expectedTrace: string[]
  expectedFinalStatus: string
}

export interface BundleValidationError {
  code: string
  location: string
  message: string
}

export interface BundleValidationResult {
  valid: boolean
  errors: BundleValidationError[]
}

export interface ImportDraftResult {
  manifest: BundleManifest
  specJson: unknown
  sampleOrg: SampleOrgSnapshot
  testCases: TestCaseSnapshot[]
  validation: BundleValidationResult
}
