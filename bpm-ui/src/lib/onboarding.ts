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

/* Approvals — mirrors spec_schema.md §2.5 */
export type ApprovalRule =
  | { type: 'direct_manager' }
  | { type: 'role'; role: string }
  | { type: 'specific_user'; userId: string }
  | { type: 'department_head'; deptOf: 'applicant' }
export interface Approval {
  id: string
  rule: ApprovalRule
  fallback?: ApprovalRule
  requiresAll?: boolean
}

/* Notifications — mirrors spec_schema.md §2.6 */
export type NotifyTrigger = 'on_submit' | 'on_approve' | 'on_reject' | 'on_complete' | 'on_assign' | 'on_sla_breach'
export type NotifyRecipient =
  | { type: 'submitter' }
  | { type: 'current_approver' }
  | { type: 'role'; role: string }
  | { type: 'specific_user'; userId: string }
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

/* Test cases — mirrors spec_schema.md §2.9 */
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
  decisions: Decision[]
  approvals: Approval[]
  notifications: Notification[]
  sla: { perNode: Record<string, NodeSLA> }
  integrations: {
    identityProvider: 'csv' | 'mcp:entra'
    csvSource?: { url: string }
    fieldMappings?: Record<string, string>
  }
  testCases: TestCase[]
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
        { id: 'archive_note', label: { 'zh-TW': '備案備註' }, type: 'textarea', required: false },
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
    { id: 'approval_manager', rule: { type: 'direct_manager' } },
    {
      id: 'approval_vp',
      rule: { type: 'department_head', deptOf: 'applicant' },
      fallback: { type: 'role', role: 'VP' },
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
  testCases: [
    {
      id: 'tc_1',
      name: '5 天特休、直屬主管核准',
      inputs: { leave_type: '特休', date_range: { start: '2026-05-10', end: '2026-05-12' }, reason: '家裡有事' },
      expectedPath: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'task_hr_archive', 'end_1'],
      expectedApprovers: [{ nodeId: 'approval_manager', userIds: ['u_wang_manager'] }],
      expectedNotifications: [
        { trigger: 'on_assign', recipientCount: 1 },
        { trigger: 'on_complete', recipientCount: 1 },
      ],
    },
    {
      id: 'tc_2',
      name: '8 天事假、需副總加簽',
      inputs: { leave_type: '事假', date_range: { start: '2026-06-01', end: '2026-06-10' }, reason: '出國' },
      expectedPath: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'approval_vp', 'task_hr_archive', 'end_1'],
      expectedApprovers: [
        { nodeId: 'approval_manager', userIds: ['u_wang_manager'] },
        { nodeId: 'approval_vp', userIds: ['u_chen_vp'] },
      ],
    },
    {
      id: 'tc_3',
      name: '病假需附證明',
      inputs: { leave_type: '病假', date_range: { start: '2026-05-15', end: '2026-05-15' }, reason: '流感', cert: 'certificate.pdf' },
      expectedPath: ['start_1', 'task_apply', 'approval_manager', 'gateway_days', 'task_hr_archive', 'end_1'],
      expectedApprovers: [{ nodeId: 'approval_manager', userIds: ['u_wang_manager'] }],
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
    { id: 'approval_manager', rule: { type: 'direct_manager' } },
    { id: 'approval_finance', rule: { type: 'role', role: 'Finance' } },
    { id: 'approval_ceo', rule: { type: 'role', role: 'CEO' }, fallback: { type: 'role', role: 'VP' } },
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
      recipients: [{ type: 'role', role: 'Purchase' }],
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
  testCases: [
    {
      id: 'tc_1',
      name: '5000 元辦公耗材，主管核准即可',
      inputs: { vendor: '全聯辦公用品', category: 'office', amount: 5000, items: 'A4 影印紙 x 50 包\n原子筆 x 100 支', justification: 'Q2 季度耗材補充' },
      expectedPath: ['start_1', 'task_request', 'approval_manager', 'gateway_after_manager', 'task_purchase_exec', 'end_1'],
      expectedApprovers: [{ nodeId: 'approval_manager', userIds: ['u_wang_manager'] }],
      expectedNotifications: [
        { trigger: 'on_assign', recipientCount: 1 },
        { trigger: 'on_complete', recipientCount: 1 },
      ],
    },
    {
      id: 'tc_2',
      name: '50000 元 IT 設備，需主管 + 財務',
      inputs: { vendor: '聯強國際', category: 'it', amount: 50000, items: 'MacBook Air M3 13" x 1', justification: '新進工程師配機', quote_file: 'quote_50k.pdf' },
      expectedPath: ['start_1', 'task_request', 'approval_manager', 'gateway_after_manager', 'approval_finance', 'gateway_after_finance', 'task_purchase_exec', 'end_1'],
      expectedApprovers: [
        { nodeId: 'approval_manager', userIds: ['u_wang_manager'] },
        { nodeId: 'approval_finance', userIds: ['u_finance_lead'] },
      ],
    },
    {
      id: 'tc_3',
      name: '200000 元服務委外，三層核准',
      inputs: { vendor: '資安顧問公司', category: 'service', amount: 200000, items: '年度資安滲透測試', justification: 'ISO 27001 稽核要求', quote_file: 'quote_200k.pdf' },
      expectedPath: ['start_1', 'task_request', 'approval_manager', 'gateway_after_manager', 'approval_finance', 'gateway_after_finance', 'approval_ceo', 'task_purchase_exec', 'end_1'],
      expectedApprovers: [
        { nodeId: 'approval_manager', userIds: ['u_wang_manager'] },
        { nodeId: 'approval_finance', userIds: ['u_finance_lead'] },
        { nodeId: 'approval_ceo', userIds: ['u_ceo'] },
      ],
    },
    {
      id: 'tc_4',
      name: '1 萬以下不附報價單應通過、1 萬以上不附應 400',
      inputs: { vendor: '邊界測試', category: 'other', amount: 10000, items: 'boundary', justification: '邊界測試 — 沒附 quote_file 預期 400' },
      expectedHttpStatus: 400,
      expectedValidationErrors: ['quote_file is required when amount >= 10000'],
    },
  ],
}
