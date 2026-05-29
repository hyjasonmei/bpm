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

/**
 * Visible step order — 7 steps after dropping VARIABLES / SLA /
 * TRANSLATION / NOTES from the main flow (those four became schema-
 * only; AI tools still target them and the data round-trips, but the
 * wizard no longer makes the user click through their panels). NOTES
 * lives as a top-right sticky button now.
 *
 * `OnboardingStepId` keeps all 11 ids in its union so `STEP_TOOLS`
 * stays Partial-keyed cleanly and re-enabling any of the 4 hidden
 * steps is just adding the entry back to this array.
 */
export const ONBOARDING_STEPS: OnboardingStep[] = [
  { id: 'source',         en: 'SOURCE',          zh: '來源',     brief: '上傳 / 描述流程 + BPMN 骨架編輯' },
  // FORMS sits before ACCESS because the access validator derives the
  // trigger from the first user task's formCode — if FORMS hadn't been
  // filled yet, ACCESS could never pass.
  { id: 'forms',          en: 'FORMS',           zh: '表單',     brief: '每個 user task 的欄位 / 排版 / 送出按鈕 (actions)' },
  { id: 'trigger_access', en: 'ACCESS',          zh: '存取',     brief: '指定誰可啟動 / 旁觀此流程' },
  { id: 'decisions',      en: 'DECISIONS',       zh: '決策',     brief: '每個 gateway 的條件' },
  { id: 'approvers',      en: 'APPROVERS',       zh: '審核者',   brief: '每個 approval 的審核者規則 / 按鈕 (actions)' },
  { id: 'notify',         en: 'NOTIFY',          zh: '通知',     brief: '通知模板與收件人' },
  { id: 'integrations',   en: 'INTEGRATIONS',    zh: '整合',     brief: '對外 API 整合（純內部流程可空）' },
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
  /** Client-only stable React key — survives `id` edits so the input
   *  doesn't unmount/remount mid-typing. Stripped on persist; refilled
   *  by `migrateDraft` on next load. */
  _uid?: string
  options?: { value: string; label: string }[]
  conditional?: string
  /** CEL boolean over (siblings + `value`); see spec_schema.md §2.3. */
  validator?: string
  /** Free-text note for chef — used when CEL / structured props can't
   *  express the business rule (e.g. "金額很大時主管要 double check
   *  上一季的預算"). chef reads this and decides how to bake it into
   *  the generated code. Renamed from `hint`; legacy drafts are
   *  migrated on load. */
  note?: { 'zh-TW': string; en?: string }
  default?: unknown
  derivedFrom?: string
}

export interface UserTask {
  id: string
  formCode: string
  /** Flat source-of-truth for every field on this form. Validators,
   *  AI tools, and chef all key off `fields[]`. The optional `layout`
   *  below describes how to arrange / group / conditionally show them;
   *  when absent, chef renders them as a flat one-per-row list. */
  fields: FormField[]
  /** Tier 1 visual layout tree. References fields by id; never embeds
   *  the field def itself. Missing field refs render as a flat fallback. */
  layout?: LayoutChild[]
  /** Buttons chef renders at the bottom of the form. Each entry maps to
   *  a state-machine transition. Empty array → migrateDraft backfills a
   *  single `submit` action. See §TaskAction. */
  actions: TaskAction[]
  permissions: {
    submitter: 'self' | string
    viewers: string[]
  }
}

/* ── Actions (§plan-stepper-action-sticky-footer.md §3) ─────────────
 *
 * Authoring lives in StepForms (userTask) / StepApprovers (approval);
 * chef reads `task.actions[]` / `approval.actions[]` and emits the
 * matching service method + state machine transition. UI primitive
 * `<ActionFooter>` on bpm-ui surfaces them at the bottom of CaseDetail.
 *
 * `reject` collapses the v1 plan's reject + request_changes — chef
 * looks at `targetEdgeId`'s target node type to decide:
 *   - target endEvent → permanent reject (case terminates)
 *   - target userTask → send-back-for-changes (form preserved)
 *
 * `revoke` runs only on Completed cases (post-terminal); chef wires it
 * with a guard like `status == "Completed"`. Different from `cancel`,
 * which aborts mid-flow.
 */
export type TaskActionKind =
  | 'submit'       // userTask → 推進
  | 'save_draft'   // userTask → 不推進，回 inbox
  | 'approve'      // approval → 走預設成功 edge
  | 'reject'       // approval → endEvent = 永久駁回；userTask target = 退回補件
  | 'complete'     // 末端 userTask 結案
  | 'cancel'       // 進行中棄案
  | 'revoke'       // 已 Completed 後撤銷（須 guard `status == "Completed"`）
  | 'custom'       // 自由命名 + targetEdgeId 必填

export type PromptCommentMode = 'none' | 'optional' | 'required'

export interface TaskAction {
  id: string
  kind: TaskActionKind
  /** At least one locale must have a value; `labelOf(a)` falls back to
   *  kind when both are empty. */
  label: { 'zh-TW'?: string; en?: string }
  /** Required when the owning node has > 1 outgoing edge; chef can
   *  derive it from the single edge otherwise. */
  targetEdgeId?: string
  /** CEL evaluated against case context. False → button disabled. */
  guard?: string
  /** Comment textarea behaviour inside the confirm modal:
   *  - 'none' → no textarea, just yes/no
   *  - 'optional' → textarea present but submittable while empty
   *  - 'required' → textarea must be filled before Confirm enables
   *  Default 'none'. Every action gets a confirm modal — there is no
   *  separate confirm flag. */
  promptComment?: PromptCommentMode
}

/** Display label for an action — first non-empty locale, then kind. */
export function actionLabel(a: TaskAction): string {
  return a.label['zh-TW']?.trim() || a.label.en?.trim() || a.kind
}

/** Display label for a notification trigger binding. */
export function triggerLabel(t: NotifyTriggerBinding): string {
  return t.kind === 'event' ? t.event : `action:${t.actionId}`
}

/** Migrate the v1 enum trigger to the v2 binding shape. Used by
 *  `migrateDraft` and on legacy AI tool emits. */
export function legacyTriggerToBinding(t: unknown): NotifyTriggerBinding {
  if (t && typeof t === 'object' && 'kind' in t) {
    const b = t as NotifyTriggerBinding
    if (b.kind === 'event' || b.kind === 'action') return b
  }
  // v1 string enum or anything weird → event.on_submit fallback.
  if (typeof t === 'string') return { kind: 'event', event: t as NotifyTrigger }
  return { kind: 'event', event: 'on_submit' }
}

/** Flatten every action on every userTask + approval into a picker-
 *  friendly list. Used by StepNotify to bind `trigger.actionId`. */
export interface ActionRef {
  actionId: string
  /** Label visible in the picker. */
  label: string
  ownerNodeId: string
  ownerNodeLabel: string
  ownerKind: 'userTask' | 'approval'
  kind: TaskActionKind
}
export function listAllActions(draft: DraftSpec): ActionRef[] {
  const out: ActionRef[] = []
  for (const ut of draft.userTasks) {
    const node = draft.flow.nodes.find(n => n.id === ut.id)
    for (const a of ut.actions ?? []) {
      out.push({
        actionId: a.id,
        label: actionLabel(a),
        ownerNodeId: ut.id,
        ownerNodeLabel: node?.label ?? ut.id,
        ownerKind: 'userTask',
        kind: a.kind,
      })
    }
  }
  for (const ap of draft.approvals) {
    const node = draft.flow.nodes.find(n => n.id === ap.id)
    for (const a of ap.actions ?? []) {
      out.push({
        actionId: a.id,
        label: actionLabel(a),
        ownerNodeId: ap.id,
        ownerNodeLabel: node?.label ?? ap.id,
        ownerKind: 'approval',
        kind: a.kind,
      })
    }
  }
  return out
}

/** Per-kind default labels in zh-TW when migrating older drafts. */
const DEFAULT_ACTION_LABELS: Record<TaskActionKind, string> = {
  submit:     '送出',
  save_draft: '存草稿',
  approve:    '核准',
  reject:     '駁回 / 退回',
  complete:   '結案',
  cancel:     '棄案',
  revoke:     '撤銷',
  custom:     '',
}

export function defaultActionLabel(kind: TaskActionKind): string {
  return DEFAULT_ACTION_LABELS[kind]
}

function newActionId(kind: TaskActionKind): string {
  return `act_${kind}_${Math.random().toString(36).slice(2, 8)}`
}

/** Default actions for a userTask when none authored. */
export function defaultUserTaskActions(): TaskAction[] {
  return [
    { id: newActionId('submit'), kind: 'submit', label: { 'zh-TW': DEFAULT_ACTION_LABELS.submit } },
  ]
}

/** Default actions for an approval node when none authored. */
export function defaultApprovalActions(): TaskAction[] {
  return [
    { id: newActionId('approve'), kind: 'approve', label: { 'zh-TW': DEFAULT_ACTION_LABELS.approve } },
    { id: newActionId('reject'),  kind: 'reject',  label: { 'zh-TW': DEFAULT_ACTION_LABELS.reject }, promptComment: 'optional' },
  ]
}

/** Drops legacy `confirm` (every action is auto-confirmed now) and
 *  remaps `promptComment: boolean` → `'none' | 'optional' | 'required'`
 *  (true → optional, false → none). Modern values pass through. */
function migrateTaskAction(raw: TaskAction): TaskAction {
  const r = raw as Omit<TaskAction, 'promptComment'> & { confirm?: unknown; promptComment?: unknown }
  const { confirm: _legacyConfirm, promptComment: rawPc, ...rest } = r
  void _legacyConfirm
  let promptComment: PromptCommentMode | undefined
  if (rawPc === true)       promptComment = 'optional'
  else if (rawPc === false) promptComment = 'none'
  else if (rawPc === 'none' || rawPc === 'optional' || rawPc === 'required') {
    promptComment = rawPc
  }
  return { ...rest, ...(promptComment ? { promptComment } : {}) } as TaskAction
}

/* ── Form layout (Tier 1 element set) ───────────────────────────
 *
 * Authoring lives in StepForms; chef reads this tree to write the
 * generated React component. The tree is parallel to `fields[]` so
 * existing validators / AI tools don't need to walk into nested
 * groups to find field metadata.
 */

/** Width hint for a field inside a `FormRow` — maps onto a CSS
 *  12-column grid. 6 ≈ half, 4 ≈ third, 12 ≈ full. */
export type FieldColSpan = 1 | 2 | 3 | 4 | 6 | 8 | 12

export interface FieldRef {
  kind: 'fieldRef'
  /** Matches `FormField.id` on the owning UserTask. */
  id: string
  /** Defaults to 12 (full row) when this fieldRef is a direct child
   *  of a section; defaults to whatever divides the row evenly when
   *  inside a FormRow. */
  colSpan?: FieldColSpan
}

export interface FormSection {
  kind: 'section'
  id: string
  title: { 'zh-TW': string; en?: string }
  /** Optional CEL — when false the whole section is hidden at runtime. */
  condition?: string
  children: LayoutChild[]
}

export interface FormRow {
  kind: 'row'
  id: string
  condition?: string
  /** Cells in this row. Use FieldRef.colSpan to size; total per row
   *  should sum to 12 (renderer wraps over rows if larger). Only
   *  fieldRefs sit inside a row — banners and nested sections must
   *  live at the section / root level. */
  children: FieldRef[]
}

export type BannerSeverity = 'info' | 'warn' | 'danger'

export interface FormBanner {
  kind: 'banner'
  id: string
  severity: BannerSeverity
  text: { 'zh-TW': string; en?: string }
  condition?: string
}

export type BannerSeverityOrTotalFormat = 'currency' | 'number' | 'percent'

/**
 * One aggregation row at the bottom of a repeater (e.g. subtotal, VAT,
 * grand total). `formula` is a CEL expression evaluated in a context
 * where `items` is the bound list and `${var}` resolves to a flow
 * variable. chef reads these and emits the corresponding computed
 * row in the generated component.
 */
export interface FormTotal {
  id: string
  label: { 'zh-TW': string; en?: string }
  formula: string
  format?: BannerSeverityOrTotalFormat
}

/**
 * A bounded list of items, each carrying its own field set + layout
 * (Tier 2). Used for invoice line items, hardware purchase rows,
 * approver collections, etc. Nested repeaters are not supported in
 * MVP — the inner layout permits sections / rows / banners only.
 */
export interface FormRepeater {
  kind: 'repeater'
  id: string
  title: { 'zh-TW': string; en?: string }
  condition?: string
  minCount?: number
  maxCount?: number
  /** Per-item field set. Each item the user adds at runtime fills out
   *  these fields. Independent from `UserTask.fields[]`. */
  itemFields: FormField[]
  /** Layout tree for one item; mirrors `FormSection.children` but is
   *  allowed to contain bare FieldRefs, FormRows, and FormBanners
   *  (no nested sections or repeaters). */
  itemLayout: LayoutChild[]
  /** Optional summary aggregations rendered below the item list. */
  totals?: FormTotal[]
}

export type LayoutChild =
  | FieldRef
  | FormSection
  | FormRow
  | FormBanner
  | FormRepeater

/** Walk a layout tree and collect every fieldRef id that points at the
 *  outer (single-instance) field set. Field refs nested inside a
 *  repeater's itemLayout are NOT included — they reference the
 *  repeater's own `itemFields[]`, which is a different namespace. */
export function collectLayoutFieldIds(layout: LayoutChild[] | undefined): string[] {
  if (!layout) return []
  const out: string[] = []
  const walk = (nodes: LayoutChild[]) => {
    for (const n of nodes) {
      if (n.kind === 'fieldRef') out.push(n.id)
      else if (n.kind === 'section') walk(n.children)
      else if (n.kind === 'row') for (const c of n.children) out.push(c.id)
      // repeater intentionally not descended — its itemLayout closes
      // over its own itemFields[] namespace.
    }
  }
  walk(layout)
  return out
}

/** A user task is "meaningfully fillable" when the submitter is forced
 *  to provide some input. Three things count: any outer field marked
 *  required, any repeater with explicit `minCount >= 1` (forces ≥1 row),
 *  or any repeater whose itemFields contain a required field (every row
 *  the user adds forces input). `minCount` undefined or 0 alone does
 *  not satisfy. Mirrors the FORMS step validator. */
export function hasMeaningfulInput(ut: UserTask): boolean {
  if (ut.fields.some(f => f.required)) return true
  const layout = ut.layout
  if (!layout) return false
  const walk = (children: LayoutChild[]): boolean => {
    for (const c of children) {
      if (c.kind === 'repeater') {
        if ((c.minCount ?? 0) >= 1) return true
        if (c.itemFields.some(f => f.required)) return true
      } else if (c.kind === 'section') {
        if (walk(c.children)) return true
      }
    }
    return false
  }
  return walk(layout)
}

/** Generate a stable client-only React key for a field. */
export function newFieldUid(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID()
  return `uid_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 10)}`
}

/** Walk a draft and add `_uid` to every FormField (outer + repeater
 *  itemFields) that doesn't have one. Pure — returns a new draft. */
export function ensureFieldUids(draft: DraftSpec): DraftSpec {
  const withUid = (f: FormField): FormField => f._uid ? f : { ...f, _uid: newFieldUid() }
  const walkLayout = (children: LayoutChild[]): LayoutChild[] =>
    children.map(c => {
      if (c.kind === 'repeater') {
        return {
          ...c,
          itemFields: c.itemFields.map(withUid),
        }
      }
      if (c.kind === 'section') return { ...c, children: walkLayout(c.children) }
      return c
    })
  return {
    ...draft,
    userTasks: draft.userTasks.map(t => ({
      ...t,
      fields: t.fields.map(withUid),
      layout: t.layout ? walkLayout(t.layout) : t.layout,
    })),
  }
}

/** Strip every `_uid` before persisting / packing — client-only field. */
export function stripFieldUids(draft: DraftSpec): DraftSpec {
  const strip = ({ _uid: _drop, ...rest }: FormField): FormField => rest
  const walkLayout = (children: LayoutChild[]): LayoutChild[] =>
    children.map(c => {
      if (c.kind === 'repeater') return { ...c, itemFields: c.itemFields.map(strip) }
      if (c.kind === 'section') return { ...c, children: walkLayout(c.children) }
      return c
    })
  return {
    ...draft,
    userTasks: draft.userTasks.map(t => ({
      ...t,
      fields: t.fields.map(strip),
      layout: t.layout ? walkLayout(t.layout) : t.layout,
    })),
  }
}

/** Build a default single-section layout containing every field on the
 *  task. Used when the user first opens a task that has fields but no
 *  hand-authored layout. */
export function buildDefaultLayout(fields: FormField[]): LayoutChild[] {
  return [{
    kind: 'section',
    id: 'section_main',
    title: { 'zh-TW': '主要欄位' },
    children: fields.map(f => ({ kind: 'fieldRef', id: f.id })),
  }]
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

/**
 * Fallback — natural-language description of what to do when the
 * primary ActorRef resolves to no one (e.g. "主管離職時，請 chef 找代理人").
 *
 * v2 (this revision) drops the v1.1 recursive ActorRef fallback chain.
 * Structured fallback chains belong in the primary `conditional`; the
 * fallback slot is reserved for the LLM escape hatch chef reads at
 * build time. See `flowcook-wizard` spec §APPROVERS for the framing.
 */
export interface ActorRefFallback {
  text: string
}

export type ActorRef =
  | { type: 'expr'; path: ActorPath; fallback?: ActorRefFallback }
  | { type: 'principal'; ref: string; fallback?: ActorRefFallback }
  | {
      type: 'conditional'
      condition: ActorRefCondition
      then: ActorRef
      else: ActorRef
      fallback?: ActorRefFallback
    }
  | {
      type: 'collection'
      mode: 'any' | 'all'
      min_approvals?: number
      actors: ActorRef[]
      fallback?: ActorRefFallback
    }
  | { type: 'natural_language'; text: string; fallback?: ActorRefFallback }

export type ActorRefType = ActorRef['type']

/** Display order — structured options on top, natural_language as a
 *  visually deprioritised last resort (renders below a separator). */
export const ACTOR_STRUCTURED_TYPES: readonly ActorRefType[] = ['expr', 'principal', 'conditional', 'collection']
export const ACTOR_LAST_RESORT_TYPE: ActorRefType = 'natural_language'

export const ACTOR_TYPE_LABELS: Record<ActorRefType, { en: string; zh: string; brief: string }> = {
  expr:             { en: 'Org-chart path',  zh: '路徑（從提案人走）',   brief: '譬如：提案人的主管 / 部門主管' },
  principal:        { en: 'Specific principal', zh: '指定 user / dept / group / 角色', brief: '從清單挑特定對象' },
  conditional:      { en: 'Conditional',     zh: '依條件分流',            brief: '譬如：金額大走 CEO，否則走主管' },
  collection:       { en: 'Collection',      zh: '多人會簽',              brief: 'any (任一通過) / all (全簽)' },
  natural_language: { en: 'Natural language', zh: '自然語言描述',         brief: '最後手段：上面都寫不下時用，會犧牲精準度' },
}

/* Approvals — mirrors spec_schema.md §2.5 (v1.1) */
export interface Approval {
  id: string
  approver: ActorRef
  /** Decision buttons. Empty array → migrateDraft backfills
   *  `[approve, reject]`. See §TaskAction. */
  actions: TaskAction[]
}

/* Notifications — mirrors spec_schema.md §2.6 */
export type NotifyTrigger = 'on_submit' | 'on_approve' | 'on_reject' | 'on_complete' | 'on_assign' | 'on_sla_breach'

/**
 * What fires this notification. Two flavours:
 *
 * - `{ kind: 'event', event: ... }` — coarse lifecycle hook (legacy /
 *   shape stays compatible with on_submit / on_approve / etc.).
 * - `{ kind: 'action', actionId: ... }` — fires immediately after the
 *   referenced TaskAction's state-machine transition lands. Lets the
 *   author distinguish e.g. "approve" vs "approve and immediately
 *   raise PO" — each has its own action.id and its own notification.
 *
 * chef reads the discriminator to decide where to hook the dispatch.
 */
export type NotifyTriggerBinding =
  | { kind: 'event'; event: NotifyTrigger }
  | { kind: 'action'; actionId: string }

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
  trigger: NotifyTriggerBinding
  channel: ('email' | 'teams' | 'in_app')[]
  recipients: NotifyRecipient[]
  template: NotifyTemplate
  /** When set, this notification is bound to a BPMN `notify` node — it
   *  fires exactly when the flow reaches that node (position-based).
   *  When undefined, it fires on the cross-cutting `trigger` event
   *  (event-based — on_submit / on_approve / etc.). The wizard groups
   *  the two kinds in separate sections of the NOTIFY step. */
  nodeId?: string
}

/* SLA — mirrors spec_schema.md §2.7. v2 simplification:
 *  - `duration` and `escalation.after` are absolute hours (e.g. "8h") only;
 *    legacy "50%" relative form is migrated to its computed absolute
 *  - `note` is the chef-instruction escape hatch when the structured
 *    duration / escalation can't express the rule (same role as
 *    FormField.note in FORMS)
 */
export interface NodeSLA {
  duration: string
  businessHoursOnly?: boolean
  escalation?: {
    after: string
    action: 'notify' | 'reassign' | 'escalate_one_level' | 'auto_approve' | 'auto_reject'
  }
  /** Free-text instruction for chef when structured duration /
   *  escalation can't express the rule (e.g. 「夜班的 SLA 算法不同」
   *  「跨時區員工放寬 2 小時」). End-users don't see this. */
  note?: { 'zh-TW': string; en?: string }
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

/** ACCESS — the trigger is auto-derived from the first
 *  user task in the flow (v0 心智模型「第一個 task = 送單」). The
 *  `triggers[]` array still shapes the spec for future cron / webhook
 *  / mail triggers, but the wizard UI no longer asks for it. */
export interface FlowTrigger {
  id: string
  type: 'form'
  /** References a `UserTask.formCode` defined in `userTasks[]`. */
  formCode: string
}

/** Derive the (single) form trigger from the first user task in the
 *  flow. Returns null when there is no user task yet, or when the
 *  first user task lacks a formCode. Callers should treat null as
 *  「尚未可送單」 and surface a guidance message. */
export function deriveAutoTrigger(draft: DraftSpec): FlowTrigger | null {
  const firstUtNode = draft.flow.nodes.find(n => n.type === 'userTask')
  if (!firstUtNode) return null
  const ut = draft.userTasks.find(t => t.id === firstUtNode.id)
  const formCode = ut?.formCode ?? ''
  if (!formCode) return null
  return {
    id: formCode.toLowerCase().replace(/[^a-z0-9-]+/g, '-') || 'main',
    type: 'form',
    formCode,
  }
}

export interface FlowAccess {
  /** Prefixed principal refs allowed to start a new instance.
   *  Format: `${kind}:${id}` where kind ∈ user|dept|group|role.
   *  Legacy unprefixed uuids are treated as `user:` at render time. */
  launchableBy: string[]
  /** v0 wizard auto-mirrors `launchableBy` (能啟動的必然看得到).
   *  Schema kept for future power-user UI to split them. */
  visibleTo: string[]
  /** Optional observer principal refs (same prefix format). */
  watcher: string[]
}

/** Discriminator for what an entry in FlowAccess refers to. */
export type PrincipalRefKind = 'user' | 'dept' | 'group' | 'role'

export interface PrincipalRef {
  kind: PrincipalRefKind
  id: string
}

const KIND_VALUES: readonly PrincipalRefKind[] = ['user', 'dept', 'group', 'role']

/** Parse a stored ref string like `user:abc-uuid` into its parts.
 *  Unprefixed legacy strings (raw uuid) are interpreted as user. */
export function parsePrincipalRef(s: string): PrincipalRef {
  const i = s.indexOf(':')
  if (i > 0) {
    const kind = s.slice(0, i)
    const id = s.slice(i + 1)
    if ((KIND_VALUES as readonly string[]).includes(kind) && id) {
      return { kind: kind as PrincipalRefKind, id }
    }
  }
  return { kind: 'user', id: s }
}

export function formatPrincipalRef(r: PrincipalRef): string {
  return `${r.kind}:${r.id}`
}

/** VARIABLES — flow-scoped values referenced as `${var}` in later
 *  steps' expression fields. */
export interface FlowVariable {
  name: string
  defaultValue: string
  description?: string
  sensitive: boolean
}

/** Step 7 — INTEGRATIONS — each entry binds to a `serviceTask` node and
 *  declares the outbound HTTP call that node performs. v0 holds the
 *  OpenAPI URL + a free-form endpoint string; OpenAPI parsing / field-
 *  mapping editor lands in a follow-up.
 *
 *  Binding semantics: 1 IntegrationItem ↔ 1 serviceTask node. Same
 *  external system reached from N different serviceTasks → N entries
 *  (DRY-via-catalog refactor is a v2+ consideration). Legacy field
 *  `triggerNodeId` is migrated to `serviceTaskNodeId` on load.
 */
export interface IntegrationItem {
  id: string
  name: string
  baseUrl: string
  openApiUrl?: string
  endpoint?: string
  /** BPMN serviceTask node this integration is bound to. Required for
   *  the wizard to know which call this is — the SOURCE / FORMS steps
   *  should make sure each serviceTask has a corresponding integration. */
  serviceTaskNodeId?: string
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
  /** ACCESS — flow-level trigger config (single form trigger in v0). */
  triggers: FlowTrigger[]
  /** ACCESS — flow-level access principals. */
  access: FlowAccess
  /** VARIABLES — flow-scoped values. */
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
  // Legacy: FormField.hint was renamed to .note. Walk userTasks once on
  // load so the UI never sees the old shape; saving will persist as note.
  type LegacyFormField = FormField & { hint?: { 'zh-TW': string; en?: string } }
  // Older drafts predate UserTask.actions[]; backfill a default `submit`
  // so chef always sees a non-empty action set + validators stay green.
  const userTasks = (partial.userTasks ?? []).map(t => ({
    ...t,
    fields: t.fields.map(f => {
      const legacy = f as LegacyFormField
      if (!legacy.hint || legacy.note) return f
      const { hint, ...rest } = legacy
      return { ...rest, note: hint } as FormField
    }),
    actions: ((t as Partial<UserTask>).actions && (t as UserTask).actions.length > 0
      ? (t as UserTask).actions
      : defaultUserTaskActions()
    ).map(migrateTaskAction),
  }))

  // Legacy: ActorRef DSL revision — v1.1 had `role` / `user` / `group`
  // as separate types and `fallback?: ActorRef` (recursive chain).
  // v2 collapses the first three into `principal` (ref = `${kind}:${id|code}`)
  // and turns `fallback` into `{ text: string }`. Walk approvals[] so
  // existing drafts open without crashing or losing the approver.
  const approvals = (partial.approvals ?? []).map(a => ({
    ...a,
    approver: migrateActorRef(a.approver),
    actions: ((a as Partial<Approval>).actions && (a as Approval).actions.length > 0
      ? (a as Approval).actions
      : defaultApprovalActions()
    ).map(migrateTaskAction),
  })) as Approval[]

  // Legacy: IntegrationItem.triggerNodeId renamed to .serviceTaskNodeId
  // (matches the BPMN-binding semantic — each integration binds to one
  // serviceTask node).
  type LegacyIntegrationItem = IntegrationItem & { triggerNodeId?: string }
  const legacyIntegrations = partial.integrations?.items ?? []
  const integrationsItems = legacyIntegrations.map(it => {
    const legacy = it as LegacyIntegrationItem
    if (legacy.serviceTaskNodeId || !legacy.triggerNodeId) return it
    const { triggerNodeId, ...rest } = legacy
    return { ...rest, serviceTaskNodeId: triggerNodeId } as IntegrationItem
  })

  // Legacy: SLA escalation.after used to accept "50%" (relative to
  // duration). v2 collapses to absolute hours so the value is always
  // legible — convert the % form on load.
  const perNode = partial.sla?.perNode ?? {}
  const migratedSla: Record<string, NodeSLA> = {}
  for (const [nodeId, raw] of Object.entries(perNode)) {
    const sla = raw as NodeSLA
    if (!sla.escalation || !/%$/.test(sla.escalation.after)) {
      migratedSla[nodeId] = sla
      continue
    }
    const pct = parseFloat(sla.escalation.after) / 100
    const m = /^(\d+)([hd])$/.exec(sla.duration)
    if (!m || isNaN(pct)) {
      migratedSla[nodeId] = sla
      continue
    }
    const hours = parseInt(m[1], 10) * (m[2] === 'd' ? 24 : 1)
    const absHours = Math.max(1, Math.round(hours * pct))
    migratedSla[nodeId] = {
      ...sla,
      escalation: { ...sla.escalation, after: `${absHours}h` },
    }
  }

  // Legacy: Notification.trigger was a bare enum (`'on_submit'` etc).
  // v2 wraps it in `{ kind: 'event', event }` so action-bound triggers
  // can coexist. Walk notifications once on load.
  const migratedNotifications = (partial.notifications ?? []).map(n => ({
    ...n,
    trigger: legacyTriggerToBinding((n as { trigger?: unknown }).trigger),
  })) as Notification[]

  const out: DraftSpec = {
    ...EMPTY_DRAFT,
    ...partial,
    userTasks,
    approvals,
    notifications: migratedNotifications,
    integrations: {
      ...(partial.integrations ?? EMPTY_DRAFT.integrations),
      items: integrationsItems,
    },
    sla: { perNode: migratedSla },
    sampleOrg: (partial.sampleOrg && (partial.sampleOrg as SampleOrgSnapshot).users)
      ? (partial.sampleOrg as SampleOrgSnapshot)
      : emptySampleOrg(),
    testCases,
  } as DraftSpec
  // Backfill client-only stable React keys so FieldEditor / ItemFieldRow
  // don't unmount mid-typing when the user edits `id`.
  return ensureFieldUids(out)
}

/** Recursive migrator: maps any legacy ActorRef shape (or modern shape
 *  passed through unchanged) into the v2 schema. */
function migrateActorRef(raw: unknown): ActorRef {
  if (!raw || typeof raw !== 'object') {
    return { type: 'expr', path: 'submitter.manager' }
  }
  const r = raw as { type?: string; [k: string]: unknown }

  // v1.1 legacy types — collapse to `principal` with prefixed ref.
  if (r.type === 'role' && typeof r.code === 'string') {
    return { type: 'principal', ref: `role:${r.code}`, ...migratedFallback(r) }
  }
  if (r.type === 'user' && typeof r.id === 'string') {
    return { type: 'principal', ref: `user:${r.id}`, ...migratedFallback(r) }
  }
  if (r.type === 'group' && typeof r.id === 'string') {
    return { type: 'principal', ref: `group:${r.id}`, ...migratedFallback(r) }
  }

  // Modern shapes — recurse into conditional/collection children but
  // strip out recursive fallback (if present).
  if (r.type === 'conditional' && r.then && r.else && r.condition) {
    return {
      type: 'conditional',
      condition: r.condition as ActorRefCondition,
      then: migrateActorRef(r.then),
      else: migrateActorRef(r.else),
      ...migratedFallback(r),
    }
  }
  if (r.type === 'collection' && Array.isArray(r.actors)) {
    return {
      type: 'collection',
      mode: (r.mode === 'all' ? 'all' : 'any') as 'any' | 'all',
      min_approvals: typeof r.min_approvals === 'number' ? r.min_approvals : undefined,
      actors: (r.actors as unknown[]).map(migrateActorRef),
      ...migratedFallback(r),
    }
  }
  if (r.type === 'expr' && typeof r.path === 'string') {
    return { type: 'expr', path: r.path as ActorPath, ...migratedFallback(r) }
  }
  if (r.type === 'principal' && typeof r.ref === 'string') {
    return { type: 'principal', ref: r.ref, ...migratedFallback(r) }
  }
  if (r.type === 'natural_language' && typeof r.text === 'string') {
    return { type: 'natural_language', text: r.text, ...migratedFallback(r) }
  }

  // Unknown shape — fall back to a placeholder so the UI doesn't crash.
  return { type: 'expr', path: 'submitter.manager' }
}

/** Turn a legacy recursive `fallback: ActorRef` into the v2
 *  `fallback: { text }` shape with a human-readable summary; passes
 *  through a modern `{ text }` fallback unchanged. Returns an
 *  object spread-friendly partial (either `{ fallback }` or `{}`). */
function migratedFallback(r: { type?: string; [k: string]: unknown }): { fallback?: ActorRefFallback } {
  const f = r.fallback as { text?: unknown } | undefined
  if (!f) return {}
  if (typeof f.text === 'string') return { fallback: { text: f.text } }
  // Legacy recursive shape — flatten to a one-line summary so the
  // information isn't silently lost. chef will surface it as a note.
  return { fallback: { text: `(legacy fallback: ${summarizeLegacyActor(f)})` } }
}

function summarizeLegacyActor(raw: unknown): string {
  if (!raw || typeof raw !== 'object') return 'unknown'
  const r = raw as { type?: string; code?: string; id?: string; path?: string }
  switch (r.type) {
    case 'expr':        return `expr:${r.path ?? '?'}`
    case 'role':        return `role:${r.code ?? '?'}`
    case 'user':        return `user:${r.id ?? '?'}`
    case 'group':       return `group:${r.id ?? '?'}`
    case 'conditional': return 'conditional'
    case 'collection':  return 'collection'
    default:            return r.type ?? 'unknown'
  }
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
    const auto = deriveAutoTrigger(s)
    if (!auto) {
      const firstUt = s.flow.nodes.find(n => n.type === 'userTask')
      if (!firstUt) errors.push('流程沒有 user task — 至少需要一張送單表單作為啟動點（在 SOURCE 新增 user task）')
      else errors.push(`送單表單 "${firstUt.label}" 還沒指定 formCode（在 FORMS 步驟設定）`)
    }
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
      else if (!hasMeaningfulInput(ut)) errors.push(`"${n.label}" 至少要有一個必填欄位或必填 repeater (minCount ≥ 1 或內含必填欄位)`)
      if (!ut.actions || ut.actions.length === 0) {
        errors.push(`"${n.label}" 沒有任何 action 按鈕（至少要有送出 / 完成 / 棄案 其一）`)
      } else {
        for (const a of ut.actions) {
          if (!actionLabel(a)) errors.push(`"${n.label}" 的 action ${a.kind} 沒有 label`)
        }
      }
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
    const errors: string[] = []
    const approvalNodes = s.flow.nodes.filter(n => n.type === 'approval')
    if (s.approvals.length < approvalNodes.length) {
      errors.push(`還有 ${approvalNodes.length - s.approvals.length} 個審核步驟尚未配置`)
    }
    for (const n of approvalNodes) {
      const ap = s.approvals.find(a => a.id === n.id)
      if (!ap) continue
      const acts = ap.actions ?? []
      if (acts.length === 0) {
        errors.push(`"${n.label}" 沒有任何 action 按鈕（最少要 approve + reject）`)
        continue
      }
      const hasApprove = acts.some(a => a.kind === 'approve')
      const hasReject  = acts.some(a => a.kind === 'reject')
      if (!hasApprove) errors.push(`"${n.label}" 缺少 approve 行動`)
      if (!hasReject)  errors.push(`"${n.label}" 缺少 reject 行動`)
      for (const a of acts) {
        if (!actionLabel(a)) errors.push(`"${n.label}" 的 action ${a.kind} 沒有 label`)
      }
    }
    return { valid: errors.length === 0, errors }
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
  localStorage.setItem(DRAFT_KEY, JSON.stringify(stripFieldUids(d)))
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
          note: { 'zh-TW': '中英文皆可' } },
        { id: 'cert', label: { 'zh-TW': '證明文件' }, type: 'file', required: true,
          conditional: "leave_type === '病假' || leave_type === '公假'" },
      ],
      permissions: { submitter: 'self', viewers: ['self', 'manager', 'role:HR'] },
      actions: [
        { id: 'act_apply_submit', kind: 'submit', label: { 'zh-TW': '送出申請' } },
        { id: 'act_apply_draft',  kind: 'save_draft', label: { 'zh-TW': '存草稿' } },
      ],
    },
    {
      id: 'task_hr_archive',
      formCode: 'LEAVE_ARCHIVE',
      fields: [
        { id: 'archive_note', label: { 'zh-TW': '備案備註' }, type: 'textarea', required: true,
          note: { 'zh-TW': 'HR 留下處理紀錄供日後追溯' } },
      ],
      permissions: { submitter: 'role:HR', viewers: ['role:HR', 'self'] },
      actions: [
        { id: 'act_arch_complete', kind: 'complete', label: { 'zh-TW': '送出備案' } },
      ],
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
    {
      id: 'approval_manager',
      approver: { type: 'expr', path: 'submitter.manager' },
      actions: [
        { id: 'act_mgr_approve', kind: 'approve', label: { 'zh-TW': '核准' } },
        { id: 'act_mgr_reject',  kind: 'reject',  label: { 'zh-TW': '退回補件' }, promptComment: 'optional' },
      ],
    },
    {
      id: 'approval_vp',
      approver: {
        type: 'expr',
        path: 'submitter.department.head',
        fallback: { text: '若部門主管不在，請走 VP 角色（v1 結構化 fallback 已收回，由 chef 處理）' },
      },
      actions: [
        { id: 'act_vp_approve', kind: 'approve', label: { 'zh-TW': '核准' } },
        { id: 'act_vp_reject',  kind: 'reject',  label: { 'zh-TW': '駁回' }, promptComment: 'required' },
      ],
    },
  ],
  notifications: [
    {
      id: 'notify_assign_manager',
      trigger: { kind: 'event', event: 'on_assign' },
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
      trigger: { kind: 'event', event: 'on_complete' },
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
          note: { 'zh-TW': '未稅金額，整數' } },
        { id: 'items', label: { 'zh-TW': '品項明細', en: 'Items' }, type: 'textarea', required: true,
          note: { 'zh-TW': '一行一品項，含數量單價' } },
        { id: 'justification', label: { 'zh-TW': '採購理由', en: 'Justification' }, type: 'textarea', required: true },
        { id: 'quote_file', label: { 'zh-TW': '報價單', en: 'Quote' }, type: 'file', required: true,
          conditional: 'amount >= 10000', note: { 'zh-TW': '1 萬以上必附正式報價單' } },
      ],
      permissions: { submitter: 'self', viewers: ['self', 'manager', 'role:Finance', 'role:Purchase'] },
      actions: [
        { id: 'act_req_submit', kind: 'submit',     label: { 'zh-TW': '送出申請', en: 'Submit' } },
        { id: 'act_req_draft',  kind: 'save_draft', label: { 'zh-TW': '存草稿' } },
        { id: 'act_req_cancel', kind: 'cancel',     label: { 'zh-TW': '取消' } },
      ],
    },
    {
      id: 'task_purchase_exec',
      formCode: 'PURCHASE_EXEC',
      fields: [
        { id: 'po_number', label: { 'zh-TW': '採購單號', en: 'PO Number' }, type: 'text', required: true,
          note: { 'zh-TW': 'ERP 開立後填回' } },
        { id: 'expected_delivery', label: { 'zh-TW': '預計到貨日', en: 'Expected delivery' }, type: 'date', required: true },
        { id: 'exec_note', label: { 'zh-TW': '處理備註', en: 'Note' }, type: 'textarea', required: false },
      ],
      permissions: { submitter: 'role:Purchase', viewers: ['self', 'manager', 'role:Finance', 'role:Purchase'] },
      actions: [
        { id: 'act_exec_complete', kind: 'complete', label: { 'zh-TW': '完成採購' } },
      ],
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
    {
      id: 'approval_manager',
      approver: { type: 'expr', path: 'submitter.manager' },
      actions: [
        { id: 'act_mgr_approve', kind: 'approve', label: { 'zh-TW': '核准' } },
        { id: 'act_mgr_reject',  kind: 'reject',  label: { 'zh-TW': '退回補件' }, promptComment: 'optional' },
      ],
    },
    {
      id: 'approval_finance',
      approver: { type: 'principal', ref: 'role:Finance' },
      actions: [
        { id: 'act_fin_approve', kind: 'approve', label: { 'zh-TW': '核准' } },
        { id: 'act_fin_reject',  kind: 'reject',  label: { 'zh-TW': '駁回' }, promptComment: 'required' },
      ],
    },
    {
      id: 'approval_ceo',
      approver: { type: 'principal', ref: 'role:CEO', fallback: { text: '若 CEO 不在，請改 VP 角色簽核' } },
      actions: [
        { id: 'act_ceo_approve', kind: 'approve', label: { 'zh-TW': '核准' } },
        { id: 'act_ceo_reject',  kind: 'reject',  label: { 'zh-TW': '駁回' }, promptComment: 'required' },
      ],
    },
  ],
  notifications: [
    {
      id: 'notify_assign_approver',
      trigger: { kind: 'event', event: 'on_assign' },
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
      trigger: { kind: 'event', event: 'on_assign' },
      channel: ['email', 'in_app'],
      recipients: [{ type: 'principal', ref: 'role:Purchase' }],
      template: {
        subject: { 'zh-TW': '【採購待處理】{{purchase.vendor}} - {{purchase.amount}} 元' },
        body: { 'zh-TW': '案件已核准完畢，請開立 PO。\n供應商: {{purchase.vendor}}\n金額: {{purchase.amount}} 元\n\n處理頁面: {{caseUrl}}' },
        variables: ['purchase.vendor', 'purchase.amount', 'caseUrl'],
      },
    },
    {
      id: 'notify_complete',
      trigger: { kind: 'event', event: 'on_complete' },
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
