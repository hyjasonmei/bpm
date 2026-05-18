/**
 * Onboarding wizard types — mirrors bpm/spec_schema.md.
 *
 * The 9-step wizard mutates a DraftSpec; on GO LIVE the wizard packages
 * the draft into a Flow Library bundle (zip) via the bpm-svc bundle
 * pipeline (see PR-I1..PR-I8). The two embedded bundle-shaped fields —
 * `sampleOrg` and `testCases` — match `SampleOrgSnapshot` /
 * `TestCaseSnapshot` so the build payload is a near-passthrough.
 */
import type { SampleOrgSnapshot, TestCaseSnapshot } from '@/types/flowLibrary'

export type OnboardingStepId =
  | 'source' | 'trigger_access' | 'variables' | 'forms' | 'decisions'
  | 'approvers' | 'notify' | 'integrations' | 'sla' | 'translation' | 'notes'

export interface OnboardingStep {
  id: OnboardingStepId
  en: string
  zh: string
  /** AI-side one-liner describing what gets clarified here */
  brief: string
}

/** Canonical 11-step order per flowcook-wizard spec. BPMN preview lives
 *  inside SOURCE now (the old standalone STRUCTURE step was merged in). */
export const ONBOARDING_STEPS: OnboardingStep[] = [
  { id: 'source',         en: 'SOURCE',          zh: '來源',     brief: '上傳 / 描述流程 + BPMN 骨架編輯' },
  { id: 'trigger_access', en: 'TRIGGER',         zh: '觸發',     brief: '指定觸發表單與啟動 / 可見 / 旁觀者' },
  { id: 'variables',      en: 'VARIABLES',       zh: '變數',     brief: '宣告流程級變數（含敏感旗標）' },
  { id: 'forms',          en: 'FORMS',           zh: '表單',     brief: '每個 user task 的欄位' },
  { id: 'decisions',      en: 'DECISIONS',       zh: '決策',     brief: '每個 gateway 的條件' },
  { id: 'approvers',      en: 'APPROVERS',       zh: '審核者',   brief: '每個 approval 的審核者規則' },
  { id: 'notify',         en: 'NOTIFY',          zh: '通知',     brief: '通知模板與收件人' },
  { id: 'integrations',   en: 'INTEGRATIONS',    zh: '整合',     brief: '對外 API 整合與欄位映射' },
  { id: 'sla',            en: 'SLA',             zh: '時限',     brief: '每節點時限與 escalation' },
  { id: 'translation',    en: 'TRANSLATION',     zh: '翻譯',     brief: '收集所有 label，補各語系翻譯' },
  { id: 'notes',          en: 'NOTES',           zh: '備註',     brief: '給 chef / 驗收者的補充說明' },
]

/* ── Spec deliverable types (subset for now — extend as steps mature) ── */

export type FieldType =
  | 'text' | 'textarea' | 'number' | 'date' | 'daterange'
  | 'select' | 'multiselect' | 'file' | 'user_picker' | 'derived'

export interface FormField {
  id: string
  label: { 'zh-TW': string; en?: string }
  type: FieldType
  required: boolean
  options?: { value: string; label: string }[]
  conditional?: string
  /** CEL boolean over (siblings + `value`); see spec_schema.md §2.3. */
  validator?: string
  hint?: { 'zh-TW': string; en?: string }
  default?: unknown
  derivedFrom?: string
}

export interface UserTask {
  id: string
  formCode: string
  fields: FormField[]
  permissions: {
    submitter: 'self' | string
    viewers: string[]
  }
}

export interface FlowNode {
  id: string
  type: 'startEvent' | 'endEvent' | 'userTask' | 'approval' | 'gateway' | 'serviceTask' | 'notify'
  label: string
}

export interface FlowEdge {
  id: string
  source: string
  target: string
  condition?: string
  isDefault?: boolean
  label?: string
}

/* Decisions (gateway rules) — mirrors spec_schema.md §2.4 */
export interface DecisionBranch {
  edgeId: string
  condition: string
  isDefault?: boolean
}
export interface Decision {
  id: string
  type: 'exclusive' | 'parallel' | 'inclusive'
  branches: DecisionBranch[]
}

/* ── ActorRef DSL — mirrors spec_schema.md §2.10 (v1.1) ── */

export const ACTOR_PATH_WHITELIST = [
  'submitter',
  'submitter.manager',
  'submitter.manager.manager',
  'submitter.manager.manager.manager',
  'submitter.department',
  'submitter.department.head',
  'submitter.department.parent',
  'submitter.department.parent.head',
  'submitter.department.parent.parent.head',
] as const

export type ActorPath = (typeof ACTOR_PATH_WHITELIST)[number]

export type ActorRefCondition = {
  field: string
  op: '==' | '!=' | '>' | '>=' | '<' | '<=' | 'in' | 'not_in'
  value: unknown
}

export type ActorRef =
  | { type: 'expr'; path: ActorPath; fallback?: ActorRef }
  | { type: 'role'; code: string; fallback?: ActorRef }
  | { type: 'group'; id: string; fallback?: ActorRef }
  | { type: 'user'; id: string; fallback?: ActorRef }
  | {
      type: 'conditional'
      condition: ActorRefCondition
      then: ActorRef
      else: ActorRef
      fallback?: ActorRef
    }
  | {
      type: 'collection'
      mode: 'any' | 'all'
      min_approvals?: number
      actors: ActorRef[]
      fallback?: ActorRef
    }

export type ActorRefType = ActorRef['type']

export const ACTOR_TYPE_LABELS: Record<ActorRefType, { en: string; zh: string }> = {
  expr:        { en: 'Org-chart path', zh: '組織路徑' },
  role:        { en: 'Role',           zh: '角色' },
  group:       { en: 'Group',          zh: '群組' },
  user:        { en: 'Specific user',  zh: '指定使用者 (測試用)' },
  conditional: { en: 'Conditional',    zh: '條件式' },
  collection:  { en: 'Collection',     zh: '合議' },
}

/* Approvals — mirrors spec_schema.md §2.5 (v1.1) */
export interface Approval {
  id: string
  approver: ActorRef
}

/* Notifications — mirrors spec_schema.md §2.6 */
export type NotifyTrigger = 'on_submit' | 'on_approve' | 'on_reject' | 'on_complete' | 'on_assign' | 'on_sla_breach'

export type NotifyRecipient =
  | { type: 'submitter' }
  | { type: 'current_approver' }
  | ActorRef                                              // role / group / user / conditional / collection
export interface NotifyTemplate {
  subject: { 'zh-TW': string; en?: string }
  body: { 'zh-TW': string; en?: string }
  variables: string[]
}
export interface Notification {
  id: string
  trigger: NotifyTrigger
  channel: ('email' | 'teams' | 'in_app')[]
  recipients: NotifyRecipient[]
  template: NotifyTemplate
}

/* SLA — mirrors spec_schema.md §2.7 */
export interface NodeSLA {
  duration: string
  businessHoursOnly?: boolean
  escalation?: {
    after: string
    action: 'notify' | 'reassign' | 'escalate_one_level' | 'auto_approve' | 'auto_reject'
  }
}

/* Test cases — mirrors spec_schema.md §2.9.
 *
 * Legacy AI-tool input shape (`TestCase`) is kept because the
 * `emit_test_cases` Claude tool still emits it. The DraftSpec itself
 * holds the bundle-shaped `TestCaseSnapshot` form (id / name / inputs /
 * expectedTrace / expectedFinalStatus); converters between the two live
 * in `onboardingTools.ts`.
 */
export interface TestCase {
  id: string
  name: string
  inputs: Record<string, unknown>
  expectedPath?: string[]
  expectedApprovers?: { nodeId: string; userIds: string[] }[]
  expectedNotifications?: { trigger: string; recipientCount: number }[]
  expectedHttpStatus?: number
  expectedValidationErrors?: string[]
}

/** Convert the AI-tool emitted test-case shape into the bundle's snapshot shape. */
export function testCaseToSnapshot(tc: TestCase): TestCaseSnapshot {
  return {
    id: tc.id,
    name: tc.name,
    inputs: tc.inputs ?? {},
    expectedTrace: tc.expectedPath ?? [],
    expectedFinalStatus: tc.expectedHttpStatus ? `Http${tc.expectedHttpStatus}` : 'Completed',
  }
}

/** Step 2 — TRIGGER & ACCESS — single form trigger in v0/v1; the
 *  `triggers[]` array is shaped to accept additional types later. */
export interface FlowTrigger {
  id: string
  type: 'form'
  /** References a `UserTask.formCode` defined in `userTasks[]`. */
  formCode: string
}

export interface FlowAccess {
  /** Principal ids allowed to start a new instance. Free-form strings
   *  in MVP — the wizard expects them to look up against admin-svc
   *  Principal API but the UI keeps them as text in v0. */
  launchableBy: string[]
  /** Principal ids allowed to see this flow in the catalog. */
  visibleTo: string[]
  /** Optional observer principals. */
  watcher: string[]
}

/** Step 3 — VARIABLES — flow-scoped values referenced as `${var}` in
 *  later steps' expression fields. */
export interface FlowVariable {
  name: string
  defaultValue: string
  description?: string
  sensitive: boolean
}

/** Step 8 — INTEGRATIONS — outbound HTTP calls keyed by a trigger node
 *  in the flow. v0 holds the OpenAPI URL + a free-form endpoint string;
 *  parsing / field-mapping editor lands in a follow-up. */
export interface IntegrationItem {
  id: string
  name: string
  baseUrl: string
  openApiUrl?: string
  endpoint?: string
  triggerNodeId?: string
  auth?: {
    kind: 'none' | 'bearer' | 'header'
    /** masked in UI when present; persisted as plain text in v0 */
    secret?: string
  }
  fieldMappings?: Record<string, string>
}

export interface DraftSpec {
  meta: {
    schemaVersion: '1.0'
    tenant: string
    flowName: string
    flowCode: string
    flowVersion: number
    createdAt: string
    createdBy: string
    language: 'zh-TW' | 'en'
  }
  flow: { nodes: FlowNode[]; edges: FlowEdge[] }
  /** Step 2 — flow-level trigger config (single form trigger in v0). */
  triggers: FlowTrigger[]
  /** Step 2 — flow-level access principals. */
  access: FlowAccess
  /** Step 3 — flow-scoped variables. */
  variables: FlowVariable[]
  userTasks: UserTask[]
  decisions: Decision[]
  approvals: Approval[]
  notifications: Notification[]
  sla: { perNode: Record<string, NodeSLA> }
  integrations: {
    identityProvider: 'csv' | 'mcp:entra'
    csvSource?: { url: string }
    fieldMappings?: Record<string, string>
    /** Step 8 — outbound integration items added by the wizard. */
    items?: IntegrationItem[]
  }
  /** Step 10 — translation table: `labels[locale][key] = text`. Wizard
   *  collects keys from flowName, node labels, field labels, etc. */
  labels?: Record<string, Record<string, string>>
  /** Step 11 — free-form notes shown to chef + reviewer. */
  notes?: string
  /** Bundle-shaped sample org (mirrors `sample-org.json` inside the bundle). */
  sampleOrg: SampleOrgSnapshot
  /** Bundle-shaped test cases (mirrors `test-cases/*.json` inside the bundle). */
  testCases: TestCaseSnapshot[]
}

/**
 * Curated default sample-org. PR-I7 §8.6: a 4-user 1-department fixture
 * that's enough to satisfy approver paths (`submitter.manager`,
 * `submitter.department.head`, role:HR / role:VP fallbacks) so the
 * wizard's GO_LIVE validator passes out of the box.
 *
 * IDs are stable hardcoded UUIDs so re-seeding the same draft on a
 * different machine produces identical bundle bytes (the manifest sha
 * stays stable across builds — see BundleBuilder.WriteRaw timestamp pin).
 */
export function emptySampleOrg(): SampleOrgSnapshot {
  return {
    users: [
      { id: '11111111-1111-1111-1111-111111111111', email: 'employee@acme.tld', fullName: 'Emily Employee', managerId: '22222222-2222-2222-2222-222222222222', departmentId: '44444444-4444-4444-4444-444444444444' },
      { id: '22222222-2222-2222-2222-222222222222', email: 'manager@acme.tld',  fullName: 'Mike Manager',   managerId: '33333333-3333-3333-3333-333333333333', departmentId: '44444444-4444-4444-4444-444444444444' },
      { id: '33333333-3333-3333-3333-333333333333', email: 'vp@acme.tld',       fullName: 'Vera VP',        managerId: null, departmentId: '44444444-4444-4444-4444-444444444444' },
      { id: '55555555-5555-5555-5555-555555555555', email: 'hr@acme.tld',       fullName: 'Hannah HR',      managerId: null, departmentId: '44444444-4444-4444-4444-444444444444' },
    ],
    departments: [
      { id: '44444444-4444-4444-4444-444444444444', code: 'HQ', name: 'Headquarters', parentId: null, headUserId: '22222222-2222-2222-2222-222222222222' },
    ],
    groups: [],
    roleAssignments: [
      { roleCode: 'HR', principalId: '55555555-5555-5555-5555-555555555555', scope: 'tenant', scopeRef: null },
      { roleCode: 'VP', principalId: '33333333-3333-3333-3333-333333333333', scope: 'tenant', scopeRef: null },
    ],
  }
}

export const EMPTY_DRAFT: DraftSpec = {
  meta: {
    schemaVersion: '1.0',
    tenant: '',
    flowName: '',
    flowCode: '',
    flowVersion: 1,
    createdAt: new Date().toISOString(),
    createdBy: 'u_demo',
    language: 'zh-TW',
  },
  flow: { nodes: [], edges: [] },
  triggers: [],
  access: { launchableBy: [], visibleTo: [], watcher: [] },
  variables: [],
  userTasks: [],
  decisions: [],
  approvals: [],
  notifications: [],
  sla: { perNode: {} },
  integrations: { identityProvider: 'csv', items: [] },
  labels: { 'zh-TW': {}, en: {} },
  notes: '',
  sampleOrg: emptySampleOrg(),
  testCases: [],
}

/**
 * Bring an arbitrary persisted-draft shape forward to the current
 * `DraftSpec` interface. Old localStorage drafts (pre-PR-I7) lacked
 * `sampleOrg` and held `testCases` in the legacy AI-tool shape; both
 * cases collapse to deterministic defaults instead of throwing.
 */
export function migrateDraft(d: unknown): DraftSpec {
  const partial = (d ?? {}) as Partial<DraftSpec> & { testCases?: unknown }
  let testCases: TestCaseSnapshot[]
  if (Array.isArray(partial.testCases)) {
    testCases = (partial.testCases as Array<unknown>).map(raw => {
      const r = raw as Partial<TestCaseSnapshot> & Partial<TestCase>
      // Looks like the bundle/snapshot shape if it has expectedTrace.
      if (Array.isArray((r as TestCaseSnapshot).expectedTrace)) {
        return {
          id: r.id ?? '',
          name: r.name ?? '',
          inputs: (r as TestCaseSnapshot).inputs ?? {},
          expectedTrace: (r as TestCaseSnapshot).expectedTrace ?? [],
          expectedFinalStatus: (r as TestCaseSnapshot).expectedFinalStatus ?? 'Completed',
        }
      }
      // Else assume the legacy AI-tool shape and map.
      return testCaseToSnapshot(r as TestCase)
    })
  } else {
    testCases = []
  }
  return {
    ...EMPTY_DRAFT,
    ...partial,
    sampleOrg: (partial.sampleOrg && (partial.sampleOrg as SampleOrgSnapshot).users)
      ? (partial.sampleOrg as SampleOrgSnapshot)
      : emptySampleOrg(),
    testCases,
  } as DraftSpec
}

/* ── Validators (gate to next step) ── */

export type ValidationResult = { valid: boolean; errors: string[] }

export const validators: Record<OnboardingStepId, (s: DraftSpec) => ValidationResult> = {
  source: (s) => {
    // SOURCE now covers meta + flow.nodes/edges (the old STRUCTURE step
    // collapsed in per spec). v0 keeps the validator gentle: name + code
    // + at least start/end nodes; broken edges are flagged but don't
    // currently block Next (preset library guarantees them).
    const errors: string[] = []
    if (!s.meta.flowName) errors.push('尚未命名流程')
    if (!s.meta.flowCode) errors.push('尚未指定 flowCode（用於 class / table 命名）')
    if (s.flow.nodes.length < 2) errors.push('流程至少需要起點與終點')
    const hasStart = s.flow.nodes.some(n => n.type === 'startEvent')
    const hasEnd = s.flow.nodes.some(n => n.type === 'endEvent')
    if (s.flow.nodes.length >= 2 && !hasStart) errors.push('缺少起點節點 (startEvent)')
    if (s.flow.nodes.length >= 2 && !hasEnd) errors.push('缺少終點節點 (endEvent)')
    return { valid: errors.length === 0, errors }
  },
  trigger_access: (s) => {
    const errors: string[] = []
    if (s.triggers.length === 0) errors.push('至少需要一個觸發表單')
    if (s.access.launchableBy.length === 0) errors.push('需指定誰可啟動本流程')
    return { valid: errors.length === 0, errors }
  },
  variables: () => ({ valid: true, errors: [] }), // optional; flows may have none
  forms: (s) => {
    const errors: string[] = []
    const userTaskNodes = s.flow.nodes.filter(n => n.type === 'userTask')
    for (const n of userTaskNodes) {
      const ut = s.userTasks.find(t => t.id === n.id)
      if (!ut) {
        errors.push(`User task "${n.label}" 尚未配置欄位`)
        continue
      }
      if (ut.fields.length === 0) errors.push(`"${n.label}" 沒有任何欄位`)
      if (!ut.fields.some(f => f.required)) errors.push(`"${n.label}" 沒有必填欄位`)
    }
    return { valid: errors.length === 0, errors }
  },
  decisions: (s) => {
    const gatewayCount = s.flow.nodes.filter(n => n.type === 'gateway').length
    if (gatewayCount === 0) return { valid: true, errors: [] }
    if (s.decisions.length < gatewayCount) {
      return { valid: false, errors: [`還有 ${gatewayCount - s.decisions.length} 個 gateway 尚未配置規則`] }
    }
    return { valid: true, errors: [] }
  },
  approvers: (s) => {
    const approvalCount = s.flow.nodes.filter(n => n.type === 'approval').length
    if (s.approvals.length < approvalCount) {
      return { valid: false, errors: [`還有 ${approvalCount - s.approvals.length} 個審核步驟尚未配置`] }
    }
    return { valid: true, errors: [] }
  },
  notify: () => ({ valid: true, errors: [] }),       // optional
  integrations: () => ({ valid: true, errors: [] }), // optional
  sla: () => ({ valid: true, errors: [] }),          // optional
  translation: () => ({ valid: true, errors: [] }),  // optional in MVP — wizard auto-fills zh-TW from labels
  notes: () => ({ valid: true, errors: [] }),
}

/* ── Persistence (localStorage) ── */

const DRAFT_KEY = 'bpm_onboarding_draft'
const STEP_KEY = 'bpm_onboarding_step'

export function loadDraft(): DraftSpec {
  try {
    const raw = localStorage.getItem(DRAFT_KEY)
    if (!raw) return EMPTY_DRAFT
    return migrateDraft(JSON.parse(raw))
  } catch { return EMPTY_DRAFT }
}

export function saveDraft(d: DraftSpec) {
  localStorage.setItem(DRAFT_KEY, JSON.stringify(d))
}

export function loadStep(): number {
  const raw = localStorage.getItem(STEP_KEY)
  const n = raw ? Number.parseInt(raw, 10) : 0
  return Number.isFinite(n) && n >= 0 && n < ONBOARDING_STEPS.length ? n : 0
}

export function saveStep(i: number) {
  localStorage.setItem(STEP_KEY, String(i))
}

export function resetDraft() {
  localStorage.removeItem(DRAFT_KEY)
  localStorage.removeItem(STEP_KEY)
}

/* ── Presets (for demo / dogfood seed) ── */

export const LEAVE_PRESET: Partial<DraftSpec> = {
  meta: {
    ...EMPTY_DRAFT.meta,
    tenant: 'acme',
    flowName: '請假',
    flowCode: 'LEAVE',
    flowVersion: 1,
  },
  flow: {
    nodes: [
      { id: 'start_1', type: 'startEvent', label: '開始' },
      { id: 'task_apply', type: 'userTask', label: '員工申請' },
      { id: 'approval_manager', type: 'approval', label: '主管核准' },
      { id: 'gateway_days', type: 'gateway', label: '超過 7 天？' },
      { id: 'approval_vp', type: 'approval', label: '副總核准' },
      { id: 'task_hr_archive', type: 'userTask', label: 'HR 備案' },
      { id: 'end_1', type: 'endEvent', label: '完成' },
    ],
    edges: [
      { id: 'e1', source: 'start_1', target: 'task_apply' },
      { id: 'e2', source: 'task_apply', target: 'approval_manager' },
      { id: 'e3', source: 'approval_manager', target: 'gateway_days' },
      { id: 'e4', source: 'gateway_days', target: 'approval_vp', condition: 'days >= 7' },
      { id: 'e5', source: 'gateway_days', target: 'task_hr_archive', condition: 'days < 7', isDefault: true },
      { id: 'e6', source: 'approval_vp', target: 'task_hr_archive' },
      { id: 'e7', source: 'task_hr_archive', target: 'end_1' },
    ],
  },
  userTasks: [
    {
      id: 'task_apply',
      formCode: 'LEAVE_APPLY',
      fields: [
        { id: 'leave_type', label: { 'zh-TW': '假別' }, type: 'select', required: true,
          options: [
            { value: '特休', label: '特休' },
            { value: '病假', label: '病假' },
            { value: '事假', label: '事假' },
            { value: '公假', label: '公假' },
          ] },
        { id: 'date_range', label: { 'zh-TW': '起訖時間' }, type: 'daterange', required: true },
        { id: 'days', label: { 'zh-TW': '天數' }, type: 'derived', required: false,
          derivedFrom: 'businessDaysBetween(date_range.start, date_range.end)' },
        { id: 'reason', label: { 'zh-TW': '事由', en: 'Reason' }, type: 'textarea', required: true,
          hint: { 'zh-TW': '中英文皆可' } },
        { id: 'cert', label: { 'zh-TW': '證明文件' }, type: 'file', required: true,
          conditional: "leave_type === '病假' || leave_type === '公假'" },
      ],
      permissions: { submitter: 'self', viewers: ['self', 'manager', 'role:HR'] },
    },
    {
      id: 'task_hr_archive',
      formCode: 'LEAVE_ARCHIVE',
      fields: [
        { id: 'archive_note', label: { 'zh-TW': '備案備註' }, type: 'textarea', required: true,
          hint: { 'zh-TW': 'HR 留下處理紀錄供日後追溯' } },
      ],
      permissions: { submitter: 'role:HR', viewers: ['role:HR', 'self'] },
    },
  ],
  decisions: [
    {
      id: 'gateway_days',
      type: 'exclusive',
      branches: [
        { edgeId: 'e4', condition: 'days >= 7' },
        { edgeId: 'e5', condition: 'days < 7', isDefault: true },
      ],
    },
  ],
  approvals: [
    { id: 'approval_manager', approver: { type: 'expr', path: 'submitter.manager' } },
    {
      id: 'approval_vp',
      approver: {
        type: 'expr',
        path: 'submitter.department.head',
        fallback: { type: 'role', code: 'VP' },
      },
    },
  ],
  notifications: [
    {
      id: 'notify_assign_manager',
      trigger: 'on_assign',
      channel: ['email', 'in_app'],
      recipients: [{ type: 'current_approver' }],
      template: {
        subject: { 'zh-TW': '【請假待簽】{{applicant.name}} 申請 {{leave.days}} 天 {{leave.type}}' },
        body: { 'zh-TW': '申請人: {{applicant.name}}\n假別: {{leave.type}}\n期間: {{leave.start}} - {{leave.end}}\n事由: {{leave.reason}}\n\n請點此核准: {{caseUrl}}' },
        variables: ['applicant.name', 'leave.days', 'leave.type', 'leave.start', 'leave.end', 'leave.reason', 'caseUrl'],
      },
    },
    {
      id: 'notify_complete',
      trigger: 'on_complete',
      channel: ['email'],
      recipients: [{ type: 'submitter' }],
      template: {
        subject: { 'zh-TW': '您的請假已備案' },
        body: { 'zh-TW': '您於 {{submitDate}} 申請的 {{leave.days}} 天 {{leave.type}} 已完成備案。' },
        variables: ['submitDate', 'leave.days', 'leave.type'],
      },
    },
  ],
  sla: {
    perNode: {
      approval_manager: { duration: '8h', businessHoursOnly: true, escalation: { after: '8h', action: 'notify' } },
      approval_vp:      { duration: '24h', businessHoursOnly: true, escalation: { after: '24h', action: 'notify' } },
    },
  },
  integrations: {
    identityProvider: 'csv',
    csvSource: { url: 's3://bpm-tenants/acme/employees-2026-05-02.csv' },
    fieldMappings: {
      employeeId: 'empId', displayName: 'name', email: 'email',
      reportsTo: 'manager', department: 'department', title: 'title',
    },
  },
  sampleOrg: emptySampleOrg(),
  testCases: [
    {
      id: 'tc_1',
      name: '5 天特休、直屬主管核准',
      inputs: { leave_type: '特休', date_range: { start: '2026-05-10', end: '2026-05-12' }, reason: '家裡有事' },
      expectedTrace: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'task_hr_archive', 'end_1'],
      expectedFinalStatus: 'Completed',
    },
    {
      id: 'tc_2',
      name: '8 天事假、需副總加簽',
      inputs: { leave_type: '事假', date_range: { start: '2026-06-01', end: '2026-06-10' }, reason: '出國' },
      expectedTrace: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'approval_vp', 'task_hr_archive', 'end_1'],
      expectedFinalStatus: 'Completed',
    },
    {
      id: 'tc_3',
      name: '病假需附證明',
      inputs: { leave_type: '病假', date_range: { start: '2026-05-15', end: '2026-05-15' }, reason: '流感', cert: 'certificate.pdf' },
      expectedTrace: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'task_hr_archive', 'end_1'],
      expectedFinalStatus: 'Completed',
    },
  ],
}

export const PURCHASE_PRESET: Partial<DraftSpec> = {
  meta: {
    ...EMPTY_DRAFT.meta,
    tenant: 'acme',
    flowName: '採購申請',
    flowCode: 'PURCHASE',
    flowVersion: 1,
  },
  flow: {
    nodes: [
      { id: 'start_1', type: 'startEvent', label: '開始' },
      { id: 'task_request', type: 'userTask', label: '員工申請' },
      { id: 'approval_manager', type: 'approval', label: '主管核准' },
      { id: 'gateway_after_manager', type: 'gateway', label: '金額 ≥ 1 萬？' },
      { id: 'approval_finance', type: 'approval', label: '財務核准' },
      { id: 'gateway_after_finance', type: 'gateway', label: '金額 ≥ 10 萬？' },
      { id: 'approval_ceo', type: 'approval', label: 'CEO 核准' },
      { id: 'task_purchase_exec', type: 'userTask', label: '採購處理' },
      { id: 'end_1', type: 'endEvent', label: '完成' },
    ],
    edges: [
      { id: 'e1', source: 'start_1', target: 'task_request' },
      { id: 'e2', source: 'task_request', target: 'approval_manager' },
      { id: 'e3', source: 'approval_manager', target: 'gateway_after_manager' },
      { id: 'e4', source: 'gateway_after_manager', target: 'task_purchase_exec', condition: 'amount < 10000', isDefault: true, label: '小額直接執行' },
      { id: 'e5', source: 'gateway_after_manager', target: 'approval_finance', condition: 'amount >= 10000', label: '需財務核准' },
      { id: 'e6', source: 'approval_finance', target: 'gateway_after_finance' },
      { id: 'e7', source: 'gateway_after_finance', target: 'task_purchase_exec', condition: 'amount < 100000', isDefault: true, label: '中額執行' },
      { id: 'e8', source: 'gateway_after_finance', target: 'approval_ceo', condition: 'amount >= 100000', label: '大額需 CEO' },
      { id: 'e9', source: 'approval_ceo', target: 'task_purchase_exec' },
      { id: 'e10', source: 'task_purchase_exec', target: 'end_1' },
    ],
  },
  userTasks: [
    {
      id: 'task_request',
      formCode: 'PURCHASE_REQUEST',
      fields: [
        { id: 'vendor', label: { 'zh-TW': '供應商', en: 'Vendor' }, type: 'text', required: true },
        { id: 'category', label: { 'zh-TW': '採購類別', en: 'Category' }, type: 'select', required: true,
          options: [
            { value: 'office', label: '辦公耗材' },
            { value: 'it', label: 'IT 設備' },
            { value: 'service', label: '服務委外' },
            { value: 'other', label: '其他' },
          ] },
        { id: 'amount', label: { 'zh-TW': '金額 (TWD)', en: 'Amount (TWD)' }, type: 'number', required: true,
          hint: { 'zh-TW': '未稅金額，整數' } },
        { id: 'items', label: { 'zh-TW': '品項明細', en: 'Items' }, type: 'textarea', required: true,
          hint: { 'zh-TW': '一行一品項，含數量單價' } },
        { id: 'justification', label: { 'zh-TW': '採購理由', en: 'Justification' }, type: 'textarea', required: true },
        { id: 'quote_file', label: { 'zh-TW': '報價單', en: 'Quote' }, type: 'file', required: true,
          conditional: 'amount >= 10000', hint: { 'zh-TW': '1 萬以上必附正式報價單' } },
      ],
      permissions: { submitter: 'self', viewers: ['self', 'manager', 'role:Finance', 'role:Purchase'] },
    },
    {
      id: 'task_purchase_exec',
      formCode: 'PURCHASE_EXEC',
      fields: [
        { id: 'po_number', label: { 'zh-TW': '採購單號', en: 'PO Number' }, type: 'text', required: true,
          hint: { 'zh-TW': 'ERP 開立後填回' } },
        { id: 'expected_delivery', label: { 'zh-TW': '預計到貨日', en: 'Expected delivery' }, type: 'date', required: true },
        { id: 'exec_note', label: { 'zh-TW': '處理備註', en: 'Note' }, type: 'textarea', required: false },
      ],
      permissions: { submitter: 'role:Purchase', viewers: ['self', 'manager', 'role:Finance', 'role:Purchase'] },
    },
  ],
  decisions: [
    {
      id: 'gateway_after_manager',
      type: 'exclusive',
      branches: [
        { edgeId: 'e4', condition: 'amount < 10000', isDefault: true },
        { edgeId: 'e5', condition: 'amount >= 10000' },
      ],
    },
    {
      id: 'gateway_after_finance',
      type: 'exclusive',
      branches: [
        { edgeId: 'e7', condition: 'amount < 100000', isDefault: true },
        { edgeId: 'e8', condition: 'amount >= 100000' },
      ],
    },
  ],
  approvals: [
    { id: 'approval_manager', approver: { type: 'expr', path: 'submitter.manager' } },
    { id: 'approval_finance', approver: { type: 'role', code: 'Finance' } },
    { id: 'approval_ceo', approver: { type: 'role', code: 'CEO', fallback: { type: 'role', code: 'VP' } } },
  ],
  notifications: [
    {
      id: 'notify_assign_approver',
      trigger: 'on_assign',
      channel: ['email', 'in_app'],
      recipients: [{ type: 'current_approver' }],
      template: {
        subject: { 'zh-TW': '【採購待簽】{{applicant.name}} 申請 {{purchase.amount}} 元 ({{purchase.vendor}})' },
        body: { 'zh-TW': '申請人: {{applicant.name}}\n供應商: {{purchase.vendor}}\n金額: {{purchase.amount}} 元\n類別: {{purchase.category}}\n理由: {{purchase.justification}}\n\n請點此核准: {{caseUrl}}' },
        variables: ['applicant.name', 'purchase.amount', 'purchase.vendor', 'purchase.category', 'purchase.justification', 'caseUrl'],
      },
    },
    {
      id: 'notify_assign_purchase',
      trigger: 'on_assign',
      channel: ['email', 'in_app'],
      recipients: [{ type: 'role', code: 'Purchase' }],
      template: {
        subject: { 'zh-TW': '【採購待處理】{{purchase.vendor}} - {{purchase.amount}} 元' },
        body: { 'zh-TW': '案件已核准完畢，請開立 PO。\n供應商: {{purchase.vendor}}\n金額: {{purchase.amount}} 元\n\n處理頁面: {{caseUrl}}' },
        variables: ['purchase.vendor', 'purchase.amount', 'caseUrl'],
      },
    },
    {
      id: 'notify_complete',
      trigger: 'on_complete',
      channel: ['email'],
      recipients: [{ type: 'submitter' }],
      template: {
        subject: { 'zh-TW': '您的採購申請已完成' },
        body: { 'zh-TW': '您於 {{submitDate}} 申請的 {{purchase.vendor}} {{purchase.amount}} 元已開立 PO ({{purchase.poNumber}})，預計 {{purchase.expectedDelivery}} 到貨。' },
        variables: ['submitDate', 'purchase.vendor', 'purchase.amount', 'purchase.poNumber', 'purchase.expectedDelivery'],
      },
    },
  ],
  sla: {
    perNode: {
      approval_manager:    { duration: '8h',  businessHoursOnly: true, escalation: { after: '8h',  action: 'notify' } },
      approval_finance:    { duration: '16h', businessHoursOnly: true, escalation: { after: '16h', action: 'notify' } },
      approval_ceo:        { duration: '24h', businessHoursOnly: true, escalation: { after: '24h', action: 'notify' } },
      task_purchase_exec:  { duration: '48h', businessHoursOnly: true, escalation: { after: '48h', action: 'notify' } },
    },
  },
  integrations: {
    identityProvider: 'csv',
    csvSource: { url: 's3://bpm-tenants/acme/employees-2026-05-02.csv' },
    fieldMappings: {
      employeeId: 'empId', displayName: 'name', email: 'email',
      reportsTo: 'manager', department: 'department', title: 'title',
    },
  },
  sampleOrg: emptySampleOrg(),
  testCases: [
    {
      id: 'tc_1',
      name: '5000 元辦公耗材，主管核准即可',
      inputs: { vendor: '全聯辦公用品', category: 'office', amount: 5000, items: 'A4 影印紙 x 50 包\n原子筆 x 100 支', justification: 'Q2 季度耗材補充' },
      expectedTrace: ['start_1', 'task_request', 'approval_manager', 'gateway_after_manager', 'task_purchase_exec', 'end_1'],
      expectedFinalStatus: 'Completed',
    },
    {
      id: 'tc_2',
      name: '50000 元 IT 設備，需主管 + 財務',
      inputs: { vendor: '聯強國際', category: 'it', amount: 50000, items: 'MacBook Air M3 13" x 1', justification: '新進工程師配機', quote_file: 'quote_50k.pdf' },
      expectedTrace: ['start_1', 'task_request', 'approval_manager', 'gateway_after_manager', 'approval_finance', 'gateway_after_finance', 'task_purchase_exec', 'end_1'],
      expectedFinalStatus: 'Completed',
    },
    {
      id: 'tc_3',
      name: '200000 元服務委外，三層核准',
      inputs: { vendor: '資安顧問公司', category: 'service', amount: 200000, items: '年度資安滲透測試', justification: 'ISO 27001 稽核要求', quote_file: 'quote_200k.pdf' },
      expectedTrace: ['start_1', 'task_request', 'approval_manager', 'gateway_after_manager', 'approval_finance', 'gateway_after_finance', 'approval_ceo', 'task_purchase_exec', 'end_1'],
      expectedFinalStatus: 'Completed',
    },
    {
      id: 'tc_4',
      name: '1 萬以下不附報價單應通過、1 萬以上不附應 400',
      inputs: { vendor: '邊界測試', category: 'other', amount: 10000, items: 'boundary', justification: '邊界測試 — 沒附 quote_file 預期 400' },
      expectedTrace: [],
      expectedFinalStatus: 'Http400',
    },
  ],
}
