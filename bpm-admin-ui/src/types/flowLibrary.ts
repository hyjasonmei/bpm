/**
 * Flow Library DTOs — mirrors bpm-svc/src/Api/Admin/FlowLibrary/FlowLibraryController.cs
 * + bpm-svc/src/Domain/Spec/Bundle/* + bpm-svc/src/Application/Spec/Bundle/* records.
 *
 * Hand-maintained: any backend DTO change has to land here too. The serializer
 * on the C# side uses camelCase + enums-as-camelCase-strings (see JsonOpts in
 * the controller), so all field names match the backend records exactly.
 */

export type SpecBundleStatus =
  | 'draft'
  | 'pending'
  | 'installed'
  | 'installedUnverified'
  | 'failed'
  | 'softDeleted'

export type BundleFileKind =
  | 'manifest'
  | 'spec'
  | 'bpmnXml'
  | 'specMd'
  | 'readme'
  | 'walkthrough'
  | 'changelog'
  | 'form'
  | 'notification'
  | 'sla'
  | 'actors'
  | 'sampleOrg'
  | 'testCase'
  | 'asset'
  | 'other'

export interface BundleFileEntry {
  path: string
  sha256: string
  sizeBytes: number
  kind: BundleFileKind
}

export interface BundleManifest {
  bundleSchemaVersion: number
  flowCode: string
  flowVersion: number
  exportedAt: string  // ISO timestamp
  sourceInstanceId: string
  parent: string | null
  files: BundleFileEntry[]
}

export interface FlowLibraryItemDto {
  id: string
  flowCode: string
  flowVersion: number
  status: SpecBundleStatus
  manifestChecksum: string
  parentManifestChecksum: string | null
  exportedAt: string  // ISO timestamp
  lastReproCheckAt: string | null
  lastReproCheckSummary: string | null
}

export type ReproStatus = 'pass' | 'fail'

export interface CaseResult {
  caseId: string
  status: ReproStatus
  expectedTrace: string[]
  actualTrace: string[]
  diff: string | null
}

export interface ReproReport {
  overallStatus: ReproStatus
  cases: CaseResult[]
}

export interface FlowLibraryDetailDto {
  summary: FlowLibraryItemDto
  manifest: BundleManifest
  lastReproReport: ReproReport | null
}

/* ── Sample-org snapshot (mirrors SampleOrgSnapshot.cs) ── */
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

/* ── Test-case snapshot (mirrors TestCaseSnapshot.cs) ── */
export interface TestCaseSnapshot {
  id: string
  name: string
  inputs: unknown
  expectedTrace: string[]
  expectedFinalStatus: string
}

/* ── Bundle validation result (mirrors BundleValidationResult / Error) ── */
export interface BundleValidationError {
  code: string
  location: string
  message: string
}

export interface BundleValidationResult {
  valid: boolean
  errors: BundleValidationError[]
}

/* ── Import endpoint return shapes ── */
export interface ImportInstallResult {
  id: string
  status: SpecBundleStatus
  reproReport: ReproReport | null
}

export interface ImportDraftResult {
  manifest: BundleManifest
  specJson: unknown
  sampleOrg: SampleOrgSnapshot
  testCases: TestCaseSnapshot[]
  validation: BundleValidationResult
}
