/**
 * Onboarding wizard types — mirrors bpm/spec_schema.md.
 *
 * The 9-step wizard mutates a DraftSpec; on GO LIVE the export is a complete
 * SpecDeliverable JSON, which becomes input to Claude Code per the
 * Concierge MVP pipeline.
 */

export type OnboardingStepId =
  | 'source' | 'structure' | 'forms' | 'decisions'
  | 'approvers' | 'notify' | 'sla' | 'test' | 'go_live'

export interface OnboardingStep {
  id: OnboardingStepId
  en: string
  zh: string
  /** AI-side one-liner describing what gets clarified here */
  brief: string
}

export const ONBOARDING_STEPS: OnboardingStep[] = [
  { id: 'source',    en: 'SOURCE',    zh: '來源',      brief: '上傳 / 描述流程，AI 抽出 BPMN 骨架' },
  { id: 'structure', en: 'STRUCTURE', zh: '結構',      brief: '確認節點、邊、低信心區塊' },
  { id: 'forms',     en: 'FORMS',     zh: '表單',      brief: '每個 user task 的欄位' },
  { id: 'decisions', en: 'DECISIONS', zh: '決策',      brief: '每個 gateway 的條件' },
  { id: 'approvers', en: 'APPROVERS', zh: '審核者',    brief: '每個 approval 的審核者規則' },
  { id: 'notify',    en: 'NOTIFY',    zh: '通知',      brief: '通知模板與收件人' },
  { id: 'sla',       en: 'SLA',       zh: '時限',      brief: '每節點時限與 escalation' },
  { id: 'test',      en: 'TEST',      zh: '測試',      brief: '建測資、跑視覺化驗證' },
  { id: 'go_live',   en: 'GO LIVE',   zh: '上線',      brief: '送 spec 到後台部署' },
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
  userTasks: UserTask[]
  decisions: unknown[]
  approvals: unknown[]
  notifications: unknown[]
  sla: { perNode: Record<string, unknown> }
  integrations: { identityProvider: 'csv' | 'mcp:entra' }
  testCases: unknown[]
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
  userTasks: [],
  decisions: [],
  approvals: [],
  notifications: [],
  sla: { perNode: {} },
  integrations: { identityProvider: 'csv' },
  testCases: [],
}

/* ── Validators (gate to next step) ── */

export type ValidationResult = { valid: boolean; errors: string[] }

export const validators: Record<OnboardingStepId, (s: DraftSpec) => ValidationResult> = {
  source: (s) => {
    const errors: string[] = []
    if (!s.meta.flowName) errors.push('尚未命名流程')
    if (!s.meta.flowCode) errors.push('尚未指定 flowCode（用於 class / table 命名）')
    if (s.flow.nodes.length < 2) errors.push('流程至少需要起點與終點')
    return { valid: errors.length === 0, errors }
  },
  structure: (s) => {
    const errors: string[] = []
    const hasStart = s.flow.nodes.some(n => n.type === 'startEvent')
    const hasEnd = s.flow.nodes.some(n => n.type === 'endEvent')
    if (!hasStart) errors.push('缺少起點節點 (startEvent)')
    if (!hasEnd) errors.push('缺少終點節點 (endEvent)')
    const ids = new Set(s.flow.nodes.map(n => n.id))
    for (const e of s.flow.edges) {
      if (!ids.has(e.source)) errors.push(`邊 ${e.id} 的 source 節點不存在`)
      if (!ids.has(e.target)) errors.push(`邊 ${e.id} 的 target 節點不存在`)
    }
    return { valid: errors.length === 0, errors }
  },
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
  notify: () => ({ valid: true, errors: [] }), // optional; warn but allow next
  sla: () => ({ valid: true, errors: [] }),    // optional
  test: (s) => {
    if (s.testCases.length === 0) return { valid: false, errors: ['至少建立一個測試案'] }
    return { valid: true, errors: [] }
  },
  go_live: () => ({ valid: true, errors: [] }),
}

/* ── Persistence (localStorage) ── */

const DRAFT_KEY = 'bpm_onboarding_draft'
const STEP_KEY = 'bpm_onboarding_step'

export function loadDraft(): DraftSpec {
  try {
    const raw = localStorage.getItem(DRAFT_KEY)
    if (!raw) return EMPTY_DRAFT
    return { ...EMPTY_DRAFT, ...JSON.parse(raw) }
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
        { id: 'archive_note', label: { 'zh-TW': '備案備註' }, type: 'textarea', required: false },
      ],
      permissions: { submitter: 'role:HR', viewers: ['role:HR', 'self'] },
    },
  ],
}
