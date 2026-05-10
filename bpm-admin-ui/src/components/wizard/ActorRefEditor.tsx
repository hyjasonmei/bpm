/**
 * ActorRefEditor — single recursive component for spec.json's
 * `ActorRef` discriminated union (see bpm/spec_schema.md §2.10).
 *
 * Switches on `value.type` to render the right child editor. Recurses
 * for `conditional` and `collection`. The validator-side rules
 * (path whitelist, conditional depth ≤ 3, fallback chain ≤ 1,
 * collection actors non-empty, min_approvals ≤ actors.length) are
 * enforced inline.
 */
import { useId } from 'react'
import { Field, Input, Select } from '@/components/ui/form'
import { ACTOR_PATH_WHITELIST, ACTOR_TYPE_LABELS, type ActorRef, type ActorRefCondition, type ActorRefType } from '@/lib/onboarding'

const MAX_CONDITIONAL_DEPTH = 3
const MAX_FALLBACK_DEPTH = 1

const TYPES: ActorRefType[] = ['expr', 'role', 'group', 'user', 'conditional', 'collection']

export interface ActorRefEditorProps {
  value: ActorRef
  onChange: (next: ActorRef) => void
  /** Current conditional nesting depth (managed internally on recursion). */
  conditionalDepth?: number
  /** Current fallback chain depth. */
  fallbackDepth?: number
  /** Hide the fallback editor (used when we ARE the fallback editor). */
  hideFallback?: boolean
  /** Visual heading for this slot. */
  label?: string
}

export function ActorRefEditor({
  value,
  onChange,
  conditionalDepth = 0,
  fallbackDepth = 0,
  hideFallback = false,
  label,
}: ActorRefEditorProps) {
  const headingId = useId()
  const handleTypeChange = (next: ActorRefType) => {
    if (next === value.type) return
    onChange(emptyActor(next))
  }

  return (
    <div className="rounded-md border border-rule bg-white">
      {label && (
        <div id={headingId} className="border-b border-rule bg-slate-50 px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-ink-muted">
          {label}
        </div>
      )}
      <div className="space-y-3 p-3">
        <div className="grid grid-cols-2 gap-2">
          <Field label="型別 / Type">
            <Select value={value.type} onChange={e => handleTypeChange(e.target.value as ActorRefType)}>
              {TYPES.map(t => (
                <option key={t} value={t}>{ACTOR_TYPE_LABELS[t].zh} ({t})</option>
              ))}
            </Select>
          </Field>
          <div className="self-end text-[10.5px] text-ink-faint">
            {value.type === 'user' && '⚠ 寫死 user id 僅限測試用，正式 spec 應改用 expr / role'}
            {value.type === 'conditional' && conditionalDepth >= MAX_CONDITIONAL_DEPTH - 1 && '已接近最大嵌套深度 (3)'}
          </div>
        </div>

        <BodyEditor value={value} onChange={onChange} conditionalDepth={conditionalDepth} fallbackDepth={fallbackDepth} />

        {!hideFallback && fallbackDepth < MAX_FALLBACK_DEPTH && (
          <FallbackBlock
            value={value.fallback}
            onChange={fb => onChange({ ...value, fallback: fb } as ActorRef)}
            conditionalDepth={conditionalDepth}
            fallbackDepth={fallbackDepth + 1}
          />
        )}
      </div>
    </div>
  )
}

function BodyEditor({
  value, onChange, conditionalDepth, fallbackDepth,
}: { value: ActorRef; onChange: (n: ActorRef) => void; conditionalDepth: number; fallbackDepth: number }) {
  switch (value.type) {
    case 'expr':
      return (
        <Field label="路徑 / Path">
          <Select value={value.path} onChange={e => onChange({ ...value, path: e.target.value as typeof ACTOR_PATH_WHITELIST[number] })}>
            {ACTOR_PATH_WHITELIST.map(p => <option key={p} value={p}>{p}</option>)}
          </Select>
        </Field>
      )

    case 'role':
      return (
        <Field label="角色代碼 / Role code" hint="如 Finance / CEO / VP / HR / admin / designer / viewer">
          <Input value={value.code} onChange={e => onChange({ ...value, code: e.target.value })} placeholder="Finance" />
        </Field>
      )

    case 'group':
      return (
        <Field label="群組 ID / Group id" hint="GUID — Phase B 改成 group code 自動完成">
          <Input value={value.id} onChange={e => onChange({ ...value, id: e.target.value })} placeholder="00000000-0000-0000-0000-000000000000" />
        </Field>
      )

    case 'user':
      return (
        <Field label="使用者 ID / User id" hint="僅供測試 — 正式 spec 不該寫死">
          <Input value={value.id} onChange={e => onChange({ ...value, id: e.target.value })} placeholder="00000000-..." />
        </Field>
      )

    case 'conditional':
      return (
        <ConditionalEditor value={value} onChange={onChange} conditionalDepth={conditionalDepth} fallbackDepth={fallbackDepth} />
      )

    case 'collection':
      return (
        <CollectionEditor value={value} onChange={onChange} conditionalDepth={conditionalDepth} fallbackDepth={fallbackDepth} />
      )
  }
}

function ConditionalEditor({
  value, onChange, conditionalDepth, fallbackDepth,
}: {
  value: Extract<ActorRef, { type: 'conditional' }>
  onChange: (n: ActorRef) => void
  conditionalDepth: number
  fallbackDepth: number
}) {
  const setCondition = (patch: Partial<ActorRefCondition>) =>
    onChange({ ...value, condition: { ...value.condition, ...patch } })

  const innerDepth = conditionalDepth + 1
  const reachedCap = innerDepth >= MAX_CONDITIONAL_DEPTH

  return (
    <div className="space-y-3">
      <div className="rounded border border-rule bg-slate-50 p-2">
        <p className="mb-2 text-[10.5px] font-semibold uppercase tracking-wider text-ink-muted">條件 / Condition</p>
        <div className="grid grid-cols-3 gap-2">
          <Field label="Field">
            <Input value={value.condition.field} onChange={e => setCondition({ field: e.target.value })} placeholder="amount" />
          </Field>
          <Field label="Op">
            <Select value={value.condition.op} onChange={e => setCondition({ op: e.target.value as ActorRefCondition['op'] })}>
              {(['==','!=','>','>=','<','<=','in','not_in'] as const).map(o => <option key={o} value={o}>{o}</option>)}
            </Select>
          </Field>
          <Field label="Value">
            <Input
              value={typeof value.condition.value === 'string' || typeof value.condition.value === 'number'
                ? String(value.condition.value)
                : JSON.stringify(value.condition.value)}
              onChange={e => setCondition({ value: parseValue(e.target.value) })}
              placeholder="50000"
            />
          </Field>
        </div>
      </div>

      {reachedCap ? (
        <div className="rounded border border-amber-200 bg-amber-50 p-2 text-[11px] text-amber-700">
          已達 conditional 嵌套深度上限 ({MAX_CONDITIONAL_DEPTH})。此分支不能再放 conditional —
          請改用 collection 或重整流程。
        </div>
      ) : (
        <>
          <ActorRefEditor
            value={value.then}
            onChange={t => onChange({ ...value, then: t })}
            conditionalDepth={innerDepth}
            fallbackDepth={fallbackDepth}
            label="THEN — 條件成立"
          />
          <ActorRefEditor
            value={value.else}
            onChange={el => onChange({ ...value, else: el })}
            conditionalDepth={innerDepth}
            fallbackDepth={fallbackDepth}
            label="ELSE — 條件不成立"
          />
        </>
      )}
    </div>
  )
}

function CollectionEditor({
  value, onChange, conditionalDepth, fallbackDepth,
}: {
  value: Extract<ActorRef, { type: 'collection' }>
  onChange: (n: ActorRef) => void
  conditionalDepth: number
  fallbackDepth: number
}) {
  const updateActor = (i: number, next: ActorRef) => {
    const actors = [...value.actors]
    actors[i] = next
    onChange({ ...value, actors })
  }
  const removeActor = (i: number) => {
    onChange({ ...value, actors: value.actors.filter((_, j) => j !== i) })
  }
  const addActor = () => {
    onChange({ ...value, actors: [...value.actors, emptyActor('expr')] })
  }
  const min = value.min_approvals ?? 1
  const minOver = value.mode === 'any' && min > value.actors.length
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-2">
        <Field label="模式 / Mode">
          <Select value={value.mode} onChange={e => onChange({ ...value, mode: e.target.value as 'any' | 'all' })}>
            <option value="any">any (任 N 人簽即可)</option>
            <option value="all">all (全員都要簽)</option>
          </Select>
        </Field>
        {value.mode === 'any' && (
          <Field label="最少簽核人數 / min_approvals">
            <Input
              type="number"
              min={1}
              max={value.actors.length || 1}
              value={String(min)}
              onChange={e => onChange({ ...value, min_approvals: Math.max(1, Number(e.target.value) || 1) })}
            />
          </Field>
        )}
      </div>

      {minOver && (
        <div className="rounded border border-amber-200 bg-amber-50 px-2 py-1 text-[11px] text-amber-700">
          min_approvals ({min}) 超過 actors 數量 ({value.actors.length}) — 請降低或新增 actor
        </div>
      )}

      <div className="space-y-2">
        {value.actors.map((a, i) => (
          <div key={i} className="relative">
            <button
              type="button"
              onClick={() => removeActor(i)}
              className="absolute right-2 top-2 z-10 text-[11px] text-danger hover:underline"
            >
              移除
            </button>
            <ActorRefEditor
              value={a}
              onChange={n => updateActor(i, n)}
              conditionalDepth={conditionalDepth}
              fallbackDepth={fallbackDepth}
              label={`Actor #${i + 1}`}
            />
          </div>
        ))}
        {value.actors.length === 0 && (
          <div className="rounded border border-dashed border-rule bg-slate-50 px-3 py-3 text-center text-[11px] text-ink-faint">
            尚未加任何 actor — 至少需要 1 個
          </div>
        )}
        <button
          type="button"
          onClick={addActor}
          className="w-full rounded border border-dashed border-blue-300 bg-blue-50 py-1.5 text-[11px] font-medium text-blue-700 hover:bg-blue-100"
        >
          + Add actor
        </button>
      </div>
    </div>
  )
}

function FallbackBlock({
  value, onChange, conditionalDepth, fallbackDepth,
}: {
  value: ActorRef | undefined
  onChange: (next: ActorRef | undefined) => void
  conditionalDepth: number
  fallbackDepth: number
}) {
  if (!value) {
    return (
      <button
        type="button"
        onClick={() => onChange(emptyActor('role'))}
        className="text-[11px] text-blue-600 hover:underline"
      >
        + 新增 fallback (主規則找不到人時用)
      </button>
    )
  }
  return (
    <div className="space-y-2">
      <ActorRefEditor
        value={value}
        onChange={onChange}
        conditionalDepth={conditionalDepth}
        fallbackDepth={fallbackDepth}
        hideFallback
        label="FALLBACK — 主規則找不到人時用 (限 1 層)"
      />
      <button type="button" onClick={() => onChange(undefined)} className="text-[11px] text-danger hover:underline">
        移除 fallback
      </button>
    </div>
  )
}

export function emptyActor(type: ActorRefType): ActorRef {
  switch (type) {
    case 'expr':        return { type: 'expr', path: 'submitter.manager' }
    case 'role':        return { type: 'role', code: '' }
    case 'group':       return { type: 'group', id: '' }
    case 'user':        return { type: 'user', id: '' }
    case 'conditional': return {
      type: 'conditional',
      condition: { field: 'amount', op: '>=', value: 0 },
      then: { type: 'expr', path: 'submitter.manager' },
      else: { type: 'expr', path: 'submitter.manager' },
    }
    case 'collection':  return {
      type: 'collection',
      mode: 'any',
      min_approvals: 1,
      actors: [{ type: 'expr', path: 'submitter.manager' }],
    }
  }
}

function parseValue(raw: string): unknown {
  if (raw === 'true') return true
  if (raw === 'false') return false
  if (raw === 'null') return null
  if (/^-?\d+(\.\d+)?$/.test(raw)) return Number(raw)
  if (raw.startsWith('[') || raw.startsWith('{')) {
    try { return JSON.parse(raw) } catch { return raw }
  }
  return raw
}
