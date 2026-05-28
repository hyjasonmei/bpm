import { useState } from 'react'
import { ChevronDown, Code2, Plus, Sparkles, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { ExpressionEditorModal } from '@/components/expression-editor/ExpressionEditorModal'
import {
  defaultActionLabel,
  type FlowEdge,
  type TaskAction,
  type TaskActionKind,
} from '@/lib/onboarding'

const USER_TASK_KINDS: TaskActionKind[] = ['submit', 'save_draft', 'complete', 'cancel', 'revoke', 'custom']
const APPROVAL_KINDS:  TaskActionKind[] = ['approve', 'reject', 'cancel', 'revoke', 'custom']

const KIND_HINT: Record<TaskActionKind, string> = {
  submit:     'user task → 推進到下一步',
  save_draft: 'user task → 不推進，回 Inbox 後續再填',
  approve:    'approval → 走預設成功 edge',
  reject:     'approval → target endEvent = 永久駁回；target userTask = 退回補件',
  complete:   '末端 user task 結案',
  cancel:     '進行中棄案',
  revoke:     '已 Completed 後撤銷（需 guard `status == "Completed"`）',
  custom:     '自由命名，必填 targetEdgeId',
}

interface Props {
  actions: TaskAction[]
  onChange: (next: TaskAction[]) => void
  /** Restricts the kinds offered by the "Add" picker. */
  nodeType: 'userTask' | 'approval'
  /** Outgoing edges of the node — fed into the targetEdgeId picker. */
  outgoingEdges: FlowEdge[]
  /** Map edge target node id → its label / type, used to derive the
   *  reject-semantic hint ("永久駁回 / 退回補件"). */
  edgeTargetMeta: (edgeId: string) => { label: string; type: string } | undefined
  /** CEL editor context (sibling field ids + variable names). */
  siblingFieldIds: string[]
  variableNames: string[]
}

export function ActionsEditor({
  actions, onChange, nodeType, outgoingEdges, edgeTargetMeta, siblingFieldIds, variableNames,
}: Props) {
  const allowedKinds = nodeType === 'userTask' ? USER_TASK_KINDS : APPROVAL_KINDS

  function addAction(kind: TaskActionKind) {
    const id = `act_${kind}_${Math.random().toString(36).slice(2, 6)}`
    const next: TaskAction = {
      id, kind,
      label: { 'zh-TW': defaultActionLabel(kind) },
      ...(outgoingEdges.length === 1 ? {} : { targetEdgeId: outgoingEdges[0]?.id }),
      ...(kind === 'reject' || kind === 'cancel' || kind === 'revoke' ? { promptComment: true } : {}),
      ...(kind === 'cancel' || kind === 'revoke' ? { confirm: true } : {}),
    }
    onChange([...actions, next])
  }

  function patchAction(idx: number, patch: Partial<TaskAction>) {
    onChange(actions.map((a, i) => i === idx ? { ...a, ...patch } : a))
  }

  function removeAction(idx: number) {
    onChange(actions.filter((_, i) => i !== idx))
  }

  return (
    <div className="space-y-2">
      {actions.length === 0 && (
        <div className="rounded border border-dashed border-rule p-3 text-center text-[11px] text-ink-faint">
          還沒有 action 按鈕。下方 + 加 action 起手。
        </div>
      )}
      {actions.map((a, idx) => (
        <ActionCard
          key={a.id}
          action={a}
          allowedKinds={allowedKinds}
          outgoingEdges={outgoingEdges}
          edgeTargetMeta={edgeTargetMeta}
          siblingFieldIds={siblingFieldIds}
          variableNames={variableNames}
          onPatch={p => patchAction(idx, p)}
          onRemove={() => removeAction(idx)}
        />
      ))}
      <AddActionDropdown
        allowedKinds={allowedKinds}
        existingKinds={new Set(actions.map(a => a.kind))}
        onAdd={addAction}
      />
    </div>
  )
}

function ActionCard({
  action, allowedKinds, outgoingEdges, edgeTargetMeta, siblingFieldIds, variableNames, onPatch, onRemove,
}: {
  action: TaskAction
  allowedKinds: TaskActionKind[]
  outgoingEdges: FlowEdge[]
  edgeTargetMeta: (edgeId: string) => { label: string; type: string } | undefined
  siblingFieldIds: string[]
  variableNames: string[]
  onPatch: (p: Partial<TaskAction>) => void
  onRemove: () => void
}) {
  const [guardOpen, setGuardOpen] = useState(false)
  const hint = KIND_HINT[action.kind]

  // Derive reject-semantics hint from edge target type.
  const rejectFlavor = (() => {
    if (action.kind !== 'reject') return null
    const edgeId = action.targetEdgeId ?? outgoingEdges[0]?.id
    const meta = edgeId ? edgeTargetMeta(edgeId) : undefined
    if (!meta) return null
    if (meta.type === 'endEvent') return '此 reject = 永久駁回 (target 為 endEvent)'
    if (meta.type === 'userTask') return `此 reject = 退回補件 (target = ${meta.label})`
    return `target = ${meta.label} (${meta.type})`
  })()

  return (
    <div className="rounded border border-rule bg-white p-3 space-y-2">
      <div className="flex items-start gap-2">
        <div className="flex-1 grid grid-cols-12 gap-2">
          <div className="col-span-3">
            <span className="mb-0.5 block text-[10px] font-mono uppercase tracking-[0.14em] text-ink-muted">Kind</span>
            <select
              value={action.kind}
              onChange={e => onPatch({ kind: e.target.value as TaskActionKind })}
              className="w-full rounded border border-rule bg-white px-1.5 py-1 text-xs text-ink outline-none focus:border-primary"
            >
              {allowedKinds.map(k => <option key={k} value={k}>{k}</option>)}
            </select>
          </div>
          <div className="col-span-5">
            <span className="mb-0.5 block text-[10px] font-mono uppercase tracking-[0.14em] text-ink-muted">Label (zh-TW)</span>
            <input
              value={action.label['zh-TW'] ?? ''}
              onChange={e => onPatch({ label: { ...action.label, 'zh-TW': e.target.value } })}
              placeholder={defaultActionLabel(action.kind) || '按鈕文字'}
              className="w-full rounded border border-rule bg-white px-1.5 py-1 text-xs text-ink outline-none focus:border-primary"
            />
          </div>
          <div className="col-span-4">
            <span className="mb-0.5 block text-[10px] font-mono uppercase tracking-[0.14em] text-ink-muted">Label (en)</span>
            <input
              value={action.label.en ?? ''}
              onChange={e => onPatch({ label: { ...action.label, en: e.target.value || undefined } })}
              placeholder="optional"
              className="w-full rounded border border-rule bg-white px-1.5 py-1 text-xs text-ink outline-none focus:border-primary"
            />
          </div>
        </div>
        <button
          type="button"
          onClick={onRemove}
          title="移除 action"
          className="mt-5 text-ink-faint hover:text-danger"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>

      <p className="text-[10px] text-ink-faint">{hint}</p>

      {outgoingEdges.length > 1 && (
        <div className="flex items-center gap-2">
          <span className="text-[10px] font-mono uppercase tracking-[0.14em] text-ink-muted">Target edge</span>
          <select
            value={action.targetEdgeId ?? ''}
            onChange={e => onPatch({ targetEdgeId: e.target.value || undefined })}
            className="flex-1 rounded border border-rule bg-white px-1.5 py-1 text-xs text-ink outline-none focus:border-primary"
          >
            <option value="">（必選）</option>
            {outgoingEdges.map(edge => {
              const meta = edgeTargetMeta(edge.id)
              return (
                <option key={edge.id} value={edge.id}>
                  {edge.id} → {meta ? `${meta.label} (${meta.type})` : edge.target}
                  {edge.condition ? ` · ${edge.condition}` : ''}
                </option>
              )
            })}
          </select>
        </div>
      )}

      {rejectFlavor && (
        <p className="rounded bg-warn/5 px-2 py-1 text-[10px] text-warn">{rejectFlavor}</p>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <label className="inline-flex items-center gap-1 text-[11px] text-ink-muted">
          <input
            type="checkbox"
            checked={!!action.confirm}
            onChange={e => onPatch({ confirm: e.target.checked || undefined })}
            className="h-3 w-3"
          />
          confirm modal
        </label>
        <label className="inline-flex items-center gap-1 text-[11px] text-ink-muted">
          <input
            type="checkbox"
            checked={!!action.promptComment}
            onChange={e => onPatch({ promptComment: e.target.checked || undefined })}
            className="h-3 w-3"
          />
          收 comment
        </label>
        <button
          type="button"
          onClick={() => setGuardOpen(true)}
          className={cn(
            'inline-flex items-center gap-1 rounded border px-2 py-0.5 text-[11px] transition-colors',
            action.guard
              ? 'border-primary/30 bg-primary/5 text-primary hover:border-primary'
              : 'border-dashed border-rule text-ink-muted hover:border-primary hover:text-primary',
          )}
        >
          <Code2 className="h-3 w-3" />
          guard {action.guard ? '· 已設定' : '(CEL，可空)'}
        </button>
      </div>

      <ExpressionEditorModal
        open={guardOpen}
        title={`guard for ${action.kind}`}
        shape="boolean"
        initial={action.guard ?? ''}
        placeholder="status == 'Completed'"
        contextFieldIds={siblingFieldIds}
        contextVariables={variableNames}
        onCancel={() => setGuardOpen(false)}
        onCommit={v => { onPatch({ guard: v || undefined }); setGuardOpen(false) }}
      />
    </div>
  )
}

function AddActionDropdown({
  allowedKinds, existingKinds, onAdd,
}: {
  allowedKinds: TaskActionKind[]
  existingKinds: Set<TaskActionKind>
  onAdd: (k: TaskActionKind) => void
}) {
  const [open, setOpen] = useState(false)
  return (
    <div className="relative inline-block">
      <button
        type="button"
        onClick={() => setOpen(v => !v)}
        className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-1 text-[11px] font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary"
      >
        <Plus className="h-3 w-3" /> 加 action <ChevronDown className="h-3 w-3" />
      </button>
      {open && (
        <div className="absolute z-20 mt-1 w-64 rounded-md border border-rule bg-white p-1 shadow-lg">
          {allowedKinds.map(k => {
            const dup = existingKinds.has(k) && k !== 'custom'
            return (
              <button
                key={k}
                type="button"
                onClick={() => { onAdd(k); setOpen(false) }}
                disabled={dup}
                className={cn(
                  'flex w-full items-start gap-2 rounded px-2 py-1.5 text-left text-[11px] transition-colors',
                  dup
                    ? 'cursor-not-allowed text-ink-faint'
                    : 'text-ink-muted hover:bg-slate-50 hover:text-primary',
                )}
              >
                <Sparkles className="mt-0.5 h-3 w-3 shrink-0" />
                <div className="min-w-0 flex-1">
                  <div className="font-semibold text-ink">{k}{dup && <span className="ml-1 text-[10px] text-ink-faint">(已存在)</span>}</div>
                  <div className="text-[10px] text-ink-faint">{KIND_HINT[k]}</div>
                </div>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
