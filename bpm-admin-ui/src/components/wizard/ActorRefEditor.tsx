/**
 * ActorRefEditor — single recursive component for the v2 ActorRef DSL.
 *
 *   v2 types: expr | principal | conditional | collection | natural_language
 *   v2 fallback: { text: string }  (no longer recursive)
 *
 * Structured types are surfaced first; `natural_language` renders below
 * a visual separator as a「最後手段」option per flowcook-wizard spec.
 *
 * The sub-editor for `expr` is still the v1 whitelist dropdown — a
 * dedicated ActorPathBuilderModal lands in a follow-up. `principal` is a
 * free-text `kind:id` input for now; swapping in the shared
 * PrincipalPicker (single-select mode) is the next polish pass.
 */
import { Field, Input, Select } from '@/components/ui/form'
import { NoteEditorModal } from '@/components/note-editor/NoteEditorModal'
import { Pencil, StickyNote } from 'lucide-react'
import { useState } from 'react'
import { cn } from '@/lib/cn'
import {
  ACTOR_PATH_WHITELIST,
  ACTOR_STRUCTURED_TYPES,
  ACTOR_TYPE_LABELS,
  type ActorRef,
  type ActorRefCondition,
  type ActorRefFallback,
  type ActorRefType,
} from '@/lib/onboarding'

const MAX_CONDITIONAL_DEPTH = 3

export interface ActorRefEditorProps {
  value: ActorRef
  onChange: (next: ActorRef) => void
  /** Current conditional nesting depth (managed internally on recursion). */
  conditionalDepth?: number
  /** Visual heading for this slot. */
  label?: string
}

export function ActorRefEditor({
  value,
  onChange,
  conditionalDepth = 0,
  label,
}: ActorRefEditorProps) {
  const handleTypeChange = (next: ActorRefType) => {
    if (next === value.type) return
    onChange(emptyActor(next))
  }

  return (
    <div className="rounded-md border border-rule bg-white">
      {label && (
        <div className="border-b border-rule bg-slate-50 px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-ink-muted">
          {label}
        </div>
      )}
      <div className="space-y-3 p-3">
        <Field label="型別 / Type" hint={ACTOR_TYPE_LABELS[value.type].brief}>
          <Select value={value.type} onChange={e => handleTypeChange(e.target.value as ActorRefType)}>
            <optgroup label="結構化">
              {ACTOR_STRUCTURED_TYPES.map(t => (
                <option key={t} value={t}>{ACTOR_TYPE_LABELS[t].zh}</option>
              ))}
            </optgroup>
            <optgroup label="最後手段">
              <option value="natural_language">{ACTOR_TYPE_LABELS.natural_language.zh}（最後手段）</option>
            </optgroup>
          </Select>
        </Field>

        <BodyEditor value={value} onChange={onChange} conditionalDepth={conditionalDepth} />

        <FallbackBlock
          value={value.fallback}
          onChange={fb => onChange({ ...value, fallback: fb } as ActorRef)}
          actorType={value.type}
        />
      </div>
    </div>
  )
}

function BodyEditor({
  value, onChange, conditionalDepth,
}: { value: ActorRef; onChange: (n: ActorRef) => void; conditionalDepth: number }) {
  switch (value.type) {
    case 'expr':
      return (
        <Field label="路徑 / Path" hint="從 submitter 走 org chart；接 .manager / .department / .head / .parent">
          <Select value={value.path} onChange={e => onChange({ ...value, path: e.target.value as typeof ACTOR_PATH_WHITELIST[number] })}>
            {ACTOR_PATH_WHITELIST.map(p => <option key={p} value={p}>{p}</option>)}
          </Select>
        </Field>
      )

    case 'principal':
      return (
        <Field label="Principal ref" hint="格式 kind:id，kind ∈ user/dept/group/role。下版會接 PrincipalPicker modal。">
          <Input
            value={value.ref}
            onChange={e => onChange({ ...value, ref: e.target.value })}
            placeholder="role:HR / user:<uuid> / dept:<uuid> / group:<uuid>"
          />
        </Field>
      )

    case 'conditional':
      return (
        <ConditionalEditor value={value} onChange={onChange} conditionalDepth={conditionalDepth} />
      )

    case 'collection':
      return (
        <CollectionEditor value={value} onChange={onChange} conditionalDepth={conditionalDepth} />
      )

    case 'natural_language':
      return (
        <NaturalLanguageBody value={value} onChange={onChange} />
      )
  }
}

function NaturalLanguageBody({
  value, onChange,
}: { value: Extract<ActorRef, { type: 'natural_language' }>; onChange: (n: ActorRef) => void }) {
  const [open, setOpen] = useState(false)
  const empty = !value.text.trim()
  return (
    <div>
      <p className="mb-2 rounded border border-warn/30 bg-warn/5 px-2 py-1 text-[11px] text-ink">
        ⚠ 最後手段：自然語言會犧牲精準度與可重現性。優先試結構化（路徑 / principal / conditional / collection）。
      </p>
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
          {empty ? '點此寫下這個 actor 的自然語言描述…（chef 會用 LLM 處理）' : value.text}
        </span>
        <span className="mt-0.5 inline-flex items-center gap-0.5 text-[10px] text-ink-faint group-hover:text-primary">
          <Pencil className="h-3 w-3" /> 編輯
        </span>
      </button>
      <NoteEditorModal
        open={open}
        title="自然語言 actor 描述"
        initial={value.text}
        helper={
          <>
            chef 會看這段文字，自己用 LLM 寫對應的 actor resolver code。<br />
            盡量寫具體：「找有空的副總」「同部門連續 3 件以上 → HR review」這種。
          </>
        }
        onCancel={() => setOpen(false)}
        onCommit={(v) => { onChange({ ...value, text: v }); setOpen(false) }}
      />
    </div>
  )
}

function FallbackBlock({
  value, onChange, actorType,
}: {
  value: ActorRefFallback | undefined
  onChange: (next: ActorRefFallback | undefined) => void
  actorType: ActorRefType
}) {
  const [open, setOpen] = useState(false)
  // natural_language already IS the LLM escape hatch — adding another
  // natural-language fallback wouldn't carry meaning, so suppress.
  if (actorType === 'natural_language') return null

  const empty = !value?.text?.trim()
  return (
    <div className="border-t border-rule pt-2">
      <p className="mb-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        Fallback — 主規則找不到人時用（自然語言，chef 處理）
      </p>
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
          {empty ? '點此寫下找不到人時的備援邏輯…（可選）' : value!.text}
        </span>
        <span className="mt-0.5 inline-flex items-center gap-0.5 text-[10px] text-ink-faint group-hover:text-primary">
          <Pencil className="h-3 w-3" /> 編輯
        </span>
      </button>
      {!empty && (
        <button
          type="button"
          onClick={() => onChange(undefined)}
          className="mt-1 text-[10px] text-ink-faint hover:text-danger"
        >
          移除 fallback
        </button>
      )}
      <NoteEditorModal
        open={open}
        title="Fallback — 主規則找不到人時用"
        initial={value?.text ?? ''}
        helper={
          <>
            v2 後 fallback 不再是另一條結構化規則，而是給 chef 看的自然語言補充。<br />
            譬如「若主管離職，請 chef 找直屬向上 2 級代理」「找不到時通知 HR 指派」。
          </>
        }
        onCancel={() => setOpen(false)}
        onCommit={(v) => { onChange(v ? { text: v } : undefined); setOpen(false) }}
      />
    </div>
  )
}

function ConditionalEditor({
  value, onChange, conditionalDepth,
}: {
  value: Extract<ActorRef, { type: 'conditional' }>
  onChange: (n: ActorRef) => void
  conditionalDepth: number
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
            label="THEN — 條件成立"
          />
          <ActorRefEditor
            value={value.else}
            onChange={el => onChange({ ...value, else: el })}
            conditionalDepth={innerDepth}
            label="ELSE — 條件不成立"
          />
        </>
      )}
    </div>
  )
}

function CollectionEditor({
  value, onChange, conditionalDepth,
}: {
  value: Extract<ActorRef, { type: 'collection' }>
  onChange: (n: ActorRef) => void
  conditionalDepth: number
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

export function emptyActor(type: ActorRefType): ActorRef {
  switch (type) {
    case 'expr':             return { type: 'expr', path: 'submitter.manager' }
    case 'principal':        return { type: 'principal', ref: '' }
    case 'conditional':      return {
      type: 'conditional',
      condition: { field: 'amount', op: '>=', value: 0 },
      then: { type: 'expr', path: 'submitter.manager' },
      else: { type: 'expr', path: 'submitter.manager' },
    }
    case 'collection':       return {
      type: 'collection',
      mode: 'any',
      min_approvals: 1,
      actors: [{ type: 'expr', path: 'submitter.manager' }],
    }
    case 'natural_language': return { type: 'natural_language', text: '' }
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
