import { useState } from 'react'
import { Plus, Trash2, ChevronDown, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Field, Input, Select, Checkbox } from '@/components/ui/form'
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
  const [expandedTask, setExpandedTask] = useState<string | null>(userTaskNodes[0]?.id ?? null)

  if (userTaskNodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        這個流程沒有 user task 節點，沒有表單需要設計。
      </div>
    )
  }

  const getOrCreate = (nodeId: string): UserTask => {
    const existing = draft.userTasks.find(t => t.id === nodeId)
    if (existing) return existing
    const node = draft.flow.nodes.find(n => n.id === nodeId)!
    return {
      id: nodeId,
      formCode: `${draft.meta.flowCode || 'FLOW'}_${node.label.toUpperCase().replace(/\s+/g, '_').slice(0, 12)}`,
      fields: [],
      permissions: { submitter: 'self', viewers: ['self'] },
    }
  }

  const upsertTask = (task: UserTask) => {
    const others = draft.userTasks.filter(t => t.id !== task.id)
    setDraft({ ...draft, userTasks: [...others, task] })
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-muted">
        每個 user task 都需要至少一個欄位、一個必填欄位才能往下一步。
      </p>

      {userTaskNodes.map(node => {
        const task = draft.userTasks.find(t => t.id === node.id) ?? getOrCreate(node.id)
        const expanded = expandedTask === node.id
        return (
          <div key={node.id} className="rounded-md border border-rule bg-white">
            <button
              onClick={() => setExpandedTask(expanded ? null : node.id)}
              className="flex w-full items-center justify-between px-3 py-2.5 text-left hover:bg-slate-50"
            >
              <div className="flex items-center gap-2">
                {expanded ? <ChevronDown className="h-4 w-4 text-ink-muted" /> : <ChevronRight className="h-4 w-4 text-ink-muted" />}
                <span className="text-sm font-semibold text-ink">{node.label}</span>
                <span className="font-mono text-[10px] text-ink-faint">{node.id}</span>
              </div>
              <div className="flex items-center gap-2 text-[11px]">
                <span className={cn(
                  'rounded px-2 py-0.5 font-medium',
                  task.fields.length === 0 ? 'bg-rose-50 text-rose-700' : 'bg-emerald-50 text-emerald-700',
                )}>
                  {task.fields.length} fields
                </span>
                {task.fields.some(f => f.required) ? (
                  <span className="rounded bg-blue-50 px-2 py-0.5 font-medium text-blue-700">has required</span>
                ) : (
                  <span className="rounded bg-rose-50 px-2 py-0.5 font-medium text-rose-700">no required</span>
                )}
              </div>
            </button>

            {expanded && (
              <div className="border-t border-rule p-3 space-y-3">
                <Field label="Form Code" hint="Claude Code 用此命名 React component / DB table">
                  <Input
                    value={task.formCode}
                    onChange={e => upsertTask({ ...task, formCode: e.target.value.toUpperCase() })}
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
                        upsertTask({ ...task, fields: [...task.fields, newField] })
                      }}
                      className="flex items-center gap-1 rounded bg-primary px-2 py-1 text-[11px] font-medium text-white hover:bg-blue-700"
                    >
                      <Plus className="h-3 w-3" /> Add Field
                    </button>
                  </div>

                  {task.fields.length === 0 ? (
                    <div className="rounded border border-dashed border-rule p-6 text-center text-xs text-ink-faint">
                      還沒有欄位。點 Add Field 開始，或在 chat 跟 AI 描述（Phase B）。
                    </div>
                  ) : (
                    <div className="space-y-2">
                      {task.fields.map((field, idx) => (
                        <FieldEditor
                          key={field.id}
                          field={field}
                          onChange={f => upsertTask({
                            ...task,
                            fields: task.fields.map((x, i) => i === idx ? f : x),
                          })}
                          onRemove={() => upsertTask({
                            ...task,
                            fields: task.fields.filter((_, i) => i !== idx),
                          })}
                        />
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

function FieldEditor({ field, onChange, onRemove }: {
  field: FormField
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
        {field.conditional !== undefined && (
          <div className="flex items-center gap-1.5">
            <span className="text-[11px] text-ink-muted">Conditional:</span>
            <Input
              className="h-7 w-64 font-mono text-[11px]"
              value={field.conditional}
              onChange={e => onChange({ ...field, conditional: e.target.value })}
              placeholder="leave_type === '病假'"
            />
          </div>
        )}
        {field.conditional === undefined && (
          <button
            onClick={() => onChange({ ...field, conditional: '' })}
            className="text-[11px] text-blue-600 hover:underline"
          >
            + Add conditional
          </button>
        )}
      </div>

      {field.type === 'select' && (
        <div className="pt-1">
          <p className="mb-1 text-[11px] font-medium text-ink-muted">Options:</p>
          <div className="flex flex-wrap gap-1">
            {(field.options ?? []).map((o, i) => (
              <span key={i} className="rounded bg-white border border-rule px-2 py-0.5 text-xs">
                {o.label}
                <button
                  onClick={() => onChange({ ...field, options: field.options?.filter((_, j) => j !== i) })}
                  className="ml-1 text-ink-faint hover:text-danger"
                >×</button>
              </span>
            ))}
            <button
              onClick={() => {
                const v = prompt('Option value:')
                if (!v) return
                onChange({
                  ...field,
                  options: [...(field.options ?? []), { value: v, label: v }],
                })
              }}
              className="rounded border border-dashed border-rule px-2 py-0.5 text-xs text-blue-600 hover:bg-white"
            >
              + Add option
            </button>
          </div>
        </div>
      )}

      {field.hint?.['zh-TW'] && (
        <p className="text-[11px] italic text-ink-faint">Hint: {field.hint['zh-TW']}</p>
      )}
    </div>
  )
}
