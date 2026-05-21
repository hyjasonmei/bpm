import { useEffect, useState } from 'react'
import { AlertCircle, CheckCircle2, Code2, Eye, LayoutGrid, ListChecks, Pencil, Plus, Send, StickyNote, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Field, Input, Select, Checkbox } from '@/components/ui/form'
import { OptionsEditorModal } from '@/components/options-editor/OptionsEditorModal'
import { ExpressionEditorModal } from '@/components/expression-editor/ExpressionEditorModal'
import { FormPreviewModal } from '@/components/form-preview/FormPreviewModal'
import { FormLayoutEditor } from '@/components/form-layout-editor/FormLayoutEditor'
import { NoteEditorModal } from '@/components/note-editor/NoteEditorModal'
import type { ExpressionShape } from '@/lib/expressions'
import type { DraftSpec, FormField, UserTask, FieldType } from '@/lib/onboarding'

const FIELD_TYPES: { value: FieldType; label: string }[] = [
  { value: 'text', label: 'Text' },
  { value: 'textarea', label: 'Textarea' },
  { value: 'number', label: 'Number' },
  { value: 'date', label: 'Date' },
  { value: 'daterange', label: 'Date Range' },
  { value: 'select', label: 'Select' },
  { value: 'multiselect', label: 'Multi-select' },
  { value: 'file', label: 'File' },
  { value: 'user_picker', label: 'User Picker' },
  { value: 'derived', label: 'Derived (computed)' },
]

export function StepForms({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const userTaskNodes = draft.flow.nodes.filter(n => n.type === 'userTask')
  const [activeTaskId, setActiveTaskId] = useState<string>(userTaskNodes[0]?.id ?? '')
  const [previewOpen, setPreviewOpen] = useState(false)

  if (userTaskNodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        這個流程沒有 user task 節點，沒有表單需要設計。
      </div>
    )
  }

  // Ensure activeTaskId is still valid (e.g. SOURCE deleted the node).
  const safeActiveId = userTaskNodes.find(n => n.id === activeTaskId)?.id ?? userTaskNodes[0].id
  const firstTaskId = userTaskNodes[0].id

  const buildDefault = (nodeId: string): UserTask => {
    const idx = userTaskNodes.findIndex(n => n.id === nodeId)
    const suffix = idx === 0 ? 'APPLY' : `TASK${idx + 1}`
    return {
      id: nodeId,
      formCode: `${draft.meta.flowCode || 'FLOW'}_${suffix}`,
      fields: [],
      permissions: { submitter: 'self', viewers: ['self'] },
    }
  }

  const getOrCreate = (nodeId: string): UserTask =>
    draft.userTasks.find(t => t.id === nodeId) ?? buildDefault(nodeId)

  const upsertTask = (task: UserTask) => {
    const others = draft.userTasks.filter(t => t.id !== task.id)
    setDraft({ ...draft, userTasks: [...others, task] })
  }

  // Auto-persist a UserTask entry for every node that doesn't have one
  // yet. Without this the formCode preview is only ever in-memory: a
  // user who clicks a pill but doesn't touch any field leaves
  // draft.userTasks empty, which the ACCESS step's auto-trigger then
  // can't see. Run once per missing node when the step mounts.
  useEffect(() => {
    const missing = userTaskNodes.filter(n => !draft.userTasks.find(t => t.id === n.id))
    if (missing.length === 0) return
    setDraft({
      ...draft,
      userTasks: [...draft.userTasks, ...missing.map(n => buildDefault(n.id))],
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const activeNode = userTaskNodes.find(n => n.id === safeActiveId)!
  const activeTask = draft.userTasks.find(t => t.id === safeActiveId) ?? getOrCreate(safeActiveId)
  const hasRequired = activeTask.fields.some(f => f.required)

  return (
    <div className="flex flex-col gap-4">
      <p className="text-xs text-ink-muted">
        每個 user task 需要至少一個欄位、一個必填欄位才能往下一步。
      </p>

      {/* Pill row — one per user task; first pill marked as 送單 trigger */}
      <div className="flex flex-wrap gap-1.5">
        {userTaskNodes.map(node => {
          const task = draft.userTasks.find(t => t.id === node.id) ?? getOrCreate(node.id)
          const isActive = node.id === safeActiveId
          const isTrigger = node.id === firstTaskId
          const taskValid = task.fields.length > 0 && task.fields.some(f => f.required)
          return (
            <button
              key={node.id}
              onClick={() => setActiveTaskId(node.id)}
              title={isTrigger ? '此 task 的 formCode 會做為流程啟動 trigger' : undefined}
              className={cn(
                'group flex items-center gap-2 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors',
                isActive
                  ? 'border-primary bg-primary/5 text-ink'
                  : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
              )}
            >
              {taskValid
                ? <CheckCircle2 className={cn('h-3.5 w-3.5', isActive ? 'text-good' : 'text-good/70')} />
                : <AlertCircle className={cn('h-3.5 w-3.5', isActive ? 'text-warn' : 'text-warn/70')} />}
              <span>{node.label}</span>
              {isTrigger && (
                <span className="inline-flex items-center gap-0.5 rounded bg-accent/15 px-1.5 py-0.5 font-mono text-[9px] tracking-[0.12em] uppercase text-accent">
                  <Send className="h-2.5 w-2.5" /> 送單
                </span>
              )}
              <span className={cn(
                'rounded-full px-1.5 text-[10px] font-mono',
                isActive ? 'bg-white text-ink-muted' : 'bg-slate-100 text-ink-faint',
              )}>
                {task.fields.length}
              </span>
            </button>
          )
        })}
      </div>

      {/* Active task editor */}
      <div className="space-y-3 rounded-md border border-rule bg-white p-4">
        <div className="flex items-baseline justify-between gap-3 border-b border-rule pb-3">
          <div>
            <h3 className="text-sm font-semibold text-ink">{activeNode.label}</h3>
            <span className="font-mono text-[10px] text-ink-faint">{activeNode.id}</span>
          </div>
          <div className="flex items-baseline gap-3">
            <div className="text-[11px] text-ink-muted">
              {activeTask.fields.length === 0
                ? <span className="text-warn">沒有欄位</span>
                : !hasRequired
                  ? <span className="text-warn">缺必填欄位</span>
                  : <span className="text-good">✓ OK</span>}
              {' · '}
              {activeTask.fields.length} fields, {activeTask.fields.filter(f => f.required).length} required
            </div>
            <button
              type="button"
              onClick={() => setPreviewOpen(true)}
              disabled={activeTask.fields.length === 0}
              className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-1 text-[11px] font-medium text-ink hover:border-primary hover:text-primary disabled:opacity-40 disabled:hover:border-rule disabled:hover:text-ink"
            >
              <Eye className="h-3.5 w-3.5" />
              預覽
            </button>
          </div>
        </div>

        <Field
          label="Form Code"
          required
          hint={safeActiveId === firstTaskId
            ? '此 task 的 formCode 會做為「送單」trigger 的 id — chef 也用此命名 React component / DB table'
            : 'chef 用此命名 React component / DB table'}
        >
          <Input
            value={activeTask.formCode}
            placeholder="e.g. LEAVE_APPLY"
            onChange={e => upsertTask({ ...activeTask, formCode: e.target.value.toUpperCase() })}
          />
        </Field>

        <div>
          <div className="mb-2 flex items-center justify-between">
            <span className="text-xs font-semibold text-ink">Fields</span>
            <button
              onClick={() => {
                const newField: FormField = {
                  id: `field_${Date.now().toString(36).slice(-4)}`,
                  label: { 'zh-TW': '新欄位' },
                  type: 'text',
                  required: false,
                }
                upsertTask({ ...activeTask, fields: [...activeTask.fields, newField] })
              }}
              className="flex items-center gap-1 rounded bg-primary px-2 py-1 text-[11px] font-medium text-white hover:bg-blue-700"
            >
              <Plus className="h-3 w-3" /> Add Field
            </button>
          </div>

          {activeTask.fields.length === 0 ? (
            <div className="rounded border border-dashed border-rule p-6 text-center text-xs text-ink-faint">
              還沒有欄位。點 Add Field 開始，或在 chat 跟 AI 描述。
            </div>
          ) : (
            <div className="space-y-2">
              {activeTask.fields.map((field, idx) => (
                <FieldEditor
                  key={field.id}
                  field={field}
                  siblingFieldIds={activeTask.fields.filter((_, i) => i !== idx).map(f => f.id)}
                  variableNames={draft.variables.map(v => v.name).filter(Boolean)}
                  onChange={f => upsertTask({
                    ...activeTask,
                    fields: activeTask.fields.map((x, i) => i === idx ? f : x),
                  })}
                  onRemove={() => upsertTask({
                    ...activeTask,
                    fields: activeTask.fields.filter((_, i) => i !== idx),
                  })}
                />
              ))}
            </div>
          )}
        </div>

        {/* Tier 1 layout editor — sections / rows / banners over the
            fields list above. chef reads `userTask.layout` to lay out
            the generated React component. */}
        <div className="border-t border-rule pt-3">
          <div className="mb-2 flex items-center gap-1.5">
            <LayoutGrid className="h-3.5 w-3.5 text-ink-muted" />
            <span className="text-xs font-semibold text-ink">Layout</span>
            <span className="font-mono text-[10px] text-ink-faint">
              chef 排版時讀這棵樹
            </span>
          </div>
          <FormLayoutEditor task={activeTask} onChange={upsertTask} />
        </div>
      </div>

      <FormPreviewModal
        open={previewOpen}
        task={activeTask}
        taskLabel={activeNode.label}
        onClose={() => setPreviewOpen(false)}
      />
    </div>
  )
}

function FieldEditor({ field, siblingFieldIds, variableNames, onChange, onRemove }: {
  field: FormField
  /** Other field IDs on the same form (for CEL context). */
  siblingFieldIds: string[]
  /** Flow-level variable names (no `${}` wrapper). */
  variableNames: string[]
  onChange: (f: FormField) => void
  onRemove: () => void
}) {
  return (
    <div className="rounded border border-rule bg-slate-50 p-3 space-y-2">
      <div className="flex items-start gap-2">
        <div className="flex-1 grid grid-cols-3 gap-2">
          <Field label="ID" hint="snake_case，唯一">
            <Input
              value={field.id}
              onChange={e => onChange({ ...field, id: e.target.value.toLowerCase().replace(/\s+/g, '_') })}
            />
          </Field>
          <Field label="Label (中文)" required>
            <Input
              value={field.label['zh-TW']}
              onChange={e => onChange({ ...field, label: { ...field.label, 'zh-TW': e.target.value } })}
            />
          </Field>
          <Field label="Type">
            <Select
              value={field.type}
              onChange={e => onChange({ ...field, type: e.target.value as FieldType })}
            >
              {FIELD_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
            </Select>
          </Field>
        </div>
        <button
          onClick={onRemove}
          className="mt-6 flex h-8 w-8 items-center justify-center rounded text-ink-faint hover:bg-rose-50 hover:text-danger"
          title="Remove field"
        >
          <Trash2 className="h-4 w-4" />
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-3 pt-1">
        <Checkbox
          id={`req-${field.id}`}
          checked={field.required}
          onChange={e => onChange({ ...field, required: e.target.checked })}
          label="Required"
        />
        {field.conditional === undefined && (
          <button
            onClick={() => onChange({ ...field, conditional: '' })}
            className="text-[11px] text-blue-600 hover:underline"
          >
            + 顯示條件 (Conditional)
          </button>
        )}
        {field.validator === undefined && (
          <button
            onClick={() => onChange({ ...field, validator: '' })}
            className="text-[11px] text-blue-600 hover:underline"
          >
            + 驗證規則 (Validator)
          </button>
        )}
        {field.type === 'derived' && field.derivedFrom === undefined && (
          <button
            onClick={() => onChange({ ...field, derivedFrom: '' })}
            className="text-[11px] text-blue-600 hover:underline"
          >
            + 計算公式 (Derived from)
          </button>
        )}
      </div>

      {/* CEL expression editors — each row shows a summary + button that
       *  opens the ExpressionEditorModal with snippets / context refs. */}
      {field.conditional !== undefined && (
        <ExpressionRow
          label="顯示條件 (Conditional)"
          shape="boolean"
          value={field.conditional}
          onChange={v => onChange({ ...field, conditional: v })}
          onRemove={() => {
            const { conditional: _drop, ...rest } = field
            onChange(rest as FormField)
          }}
          placeholder="leave_type == '病假' || leave_type == '公假'"
          siblingFieldIds={siblingFieldIds}
          variableNames={variableNames}
        />
      )}
      {field.validator !== undefined && (
        <ExpressionRow
          label="驗證規則 (Validator) — 可用 value 代表本欄位值"
          shape="boolean"
          value={field.validator}
          onChange={v => onChange({ ...field, validator: v })}
          onRemove={() => {
            const { validator: _drop, ...rest } = field
            onChange(rest as FormField)
          }}
          placeholder="value > 0 && value <= 10000000"
          siblingFieldIds={[...siblingFieldIds, 'value']}
          variableNames={variableNames}
        />
      )}
      {field.type === 'derived' && field.derivedFrom !== undefined && (
        <ExpressionRow
          label="計算公式 (Derived from)"
          shape="any"
          value={field.derivedFrom}
          onChange={v => onChange({ ...field, derivedFrom: v })}
          onRemove={() => {
            const { derivedFrom: _drop, ...rest } = field
            onChange(rest as FormField)
          }}
          placeholder="businessDaysBetween(date_range.start, date_range.end)"
          siblingFieldIds={siblingFieldIds}
          variableNames={variableNames}
        />
      )}

      {(field.type === 'select' || field.type === 'multiselect') && (
        <OptionsBlock field={field} onChange={onChange} />
      )}

      <NoteBlock field={field} onChange={onChange} />
    </div>
  )
}

/**
 * Always-visible note row at the bottom of every field. The note is a
 * free-text instruction for chef — used when CEL / structured props
 * can't express the rule (e.g.「金額大時主管要 double-check 上季預算」).
 * Click 編輯 → opens NoteEditorModal; AI chat can also fill this via
 * emit_form_fields.noteZh.
 */
function NoteBlock({ field, onChange }: {
  field: FormField
  onChange: (f: FormField) => void
}) {
  const [open, setOpen] = useState(false)
  const note = field.note?.['zh-TW'] ?? ''
  const empty = !note.trim()
  return (
    <div className="pt-1">
      <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        備註 / Note — 給 chef 的自然語言補充
      </span>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className={cn(
          'group flex w-full items-start gap-2 rounded border bg-white px-2 py-1.5 text-left hover:border-primary',
          empty ? 'border-dashed border-rule' : 'border-rule',
        )}
      >
        <StickyNote className={cn('mt-0.5 h-3.5 w-3.5 shrink-0', empty ? 'text-ink-faint' : 'text-primary')} />
        <span className={cn('flex-1 whitespace-pre-line text-[11px] leading-relaxed', empty ? 'text-ink-faint' : 'text-ink')}>
          {empty ? '點此寫給 chef…（譬如 CEL 寫不下的業務規則）' : note}
        </span>
        <span className="mt-0.5 inline-flex items-center gap-0.5 text-[10px] text-ink-faint group-hover:text-primary">
          <Pencil className="h-3 w-3" />
          編輯
        </span>
      </button>
      <NoteEditorModal
        open={open}
        title={`備註 — ${field.label['zh-TW'] || field.id}`}
        initial={note}
        helper={
          <>
            這段文字直接交給 chef（生 code 的 AI）讀。寫不下 CEL 的業務規則、跨欄位互動、客戶情境都可以寫這。<br />
            一般使用者看不到此內容。
          </>
        }
        onCancel={() => setOpen(false)}
        onCommit={(v) => {
          onChange({
            ...field,
            note: v ? { 'zh-TW': v, ...(field.note?.en ? { en: field.note.en } : {}) } : undefined,
          })
          setOpen(false)
        }}
      />
    </div>
  )
}

/**
 * Compact summary + "edit in modal" trigger for a field's `options`
 * (select / multiselect). Showed as inline chips so the user sees what's
 * configured at a glance; clicking 編輯選項 opens OptionsEditorModal.
 */
function OptionsBlock({ field, onChange }: {
  field: FormField
  onChange: (f: FormField) => void
}) {
  const [open, setOpen] = useState(false)
  const opts = field.options ?? []
  return (
    <div className="pt-1">
      <div className="mb-1 flex items-center gap-2">
        <p className="text-[11px] font-medium text-ink-muted">選項 (Options):</p>
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[11px] font-medium text-ink hover:border-primary hover:text-primary"
        >
          <ListChecks className="h-3 w-3" />
          {opts.length === 0 ? '新增選項…' : '編輯選項…'}
        </button>
      </div>
      {opts.length > 0 ? (
        <div className="flex flex-wrap gap-1">
          {opts.map((o, i) => (
            <span key={i} className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-xs text-ink">
              {o.label}
              <span className="font-mono text-[10px] text-ink-faint">{o.value}</span>
            </span>
          ))}
        </div>
      ) : (
        <p className="text-[11px] text-ink-faint">尚未設選項。</p>
      )}
      <OptionsEditorModal
        open={open}
        fieldLabel={field.label['zh-TW']}
        initial={opts}
        onCancel={() => setOpen(false)}
        onCommit={(next) => {
          onChange({ ...field, options: next })
          setOpen(false)
        }}
      />
    </div>
  )
}

/**
 * Compact summary chip for one CEL expression (conditional / validator /
 * derivedFrom). Shows the label, a one-line preview of the current
 * expression (or「點此設定」when empty), and opens ExpressionEditorModal
 * for editing. The remove button drops the expression back to undefined.
 */
function ExpressionRow({
  label,
  shape,
  value,
  onChange,
  onRemove,
  placeholder,
  siblingFieldIds,
  variableNames,
}: {
  label: string
  shape: ExpressionShape
  value: string
  onChange: (next: string) => void
  onRemove: () => void
  placeholder?: string
  siblingFieldIds: string[]
  variableNames: string[]
}) {
  const [open, setOpen] = useState(false)
  const empty = !value.trim()
  return (
    <div className="rounded border border-rule bg-white p-2">
      <div className="mb-1 flex items-center justify-between">
        <span className="text-[11px] font-medium text-ink-muted">{label}</span>
        <button
          onClick={onRemove}
          className="text-[10px] text-ink-faint hover:text-danger"
          title="Remove expression"
        >
          移除
        </button>
      </div>
      <button
        type="button"
        onClick={() => setOpen(true)}
        className={cn(
          'group flex w-full items-center gap-2 rounded border bg-white px-2 py-1 text-left hover:border-primary',
          empty ? 'border-dashed border-rule' : 'border-rule',
        )}
      >
        <Code2 className={cn('h-3.5 w-3.5 shrink-0', empty ? 'text-ink-faint' : 'text-primary')} />
        <code className={cn('flex-1 truncate font-mono text-[11px]', empty ? 'text-ink-faint' : 'text-ink')}>
          {empty ? '點此設定…' : value}
        </code>
        <span className="text-[10px] text-ink-faint group-hover:text-primary">編輯 →</span>
      </button>
      <ExpressionEditorModal
        open={open}
        title={label}
        shape={shape}
        initial={value}
        placeholder={placeholder}
        contextFieldIds={siblingFieldIds}
        contextVariables={variableNames}
        onCancel={() => setOpen(false)}
        onCommit={(v) => { onChange(v); setOpen(false) }}
      />
    </div>
  )
}
