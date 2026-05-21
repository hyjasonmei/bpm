/**
 * ActorPathBuilderModal — token-by-token builder for ActorRef.expr.path.
 *
 * The path is a dot-walker DSL (see flowcook-wizard spec §APPROVERS,
 * resolver in bpm-svc/Application/Spec/ActorResolver.cs). Each segment
 * is a state transition:
 *
 *   user .manager → user
 *   user .department → department
 *   department .head → user
 *   department .parent → department
 *
 * The whitelist (9 known-good paths) acts as a final sanity check; the
 * builder only offers transitions that lead to a whitelisted path.
 *
 * UI:
 *   [submitter] [.manager ▾] [.department ▾]    ← segment chips with
 *                                                   per-state dropdown
 *   路徑：submitter.manager.department
 *   語意：提案人 → 主管 → 主管所屬部門
 *
 *   常用範本（一鍵填入）
 *   [submitter.manager  主管]  [submitter.department.head 部門主管] …
 */
import { useEffect, useMemo, useState } from 'react'
import { ChevronDown, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'
import { cn } from '@/lib/cn'
import { ACTOR_PATH_WHITELIST, type ActorPath } from '@/lib/onboarding'

type State = 'user' | 'department'

interface TokenDef {
  token: string
  zh: string
  nextState: State
}

const TRANSITIONS: Record<State, TokenDef[]> = {
  user: [
    { token: 'manager', zh: '主管', nextState: 'user' },
    { token: 'department', zh: '所屬部門', nextState: 'department' },
  ],
  department: [
    { token: 'head', zh: '部門主管', nextState: 'user' },
    { token: 'parent', zh: '上級部門', nextState: 'department' },
  ],
}

/** Walk the segments left-to-right; return the state after each
 *  segment plus a friendlier description for previewing. */
function walkPath(path: string): {
  segments: string[]
  states: State[]
  description: string
  inWhitelist: boolean
} {
  const segments = path.split('.').filter(Boolean)
  const states: State[] = []
  let state: State = 'user'
  const descParts: string[] = ['提案人']
  for (let i = 0; i < segments.length; i++) {
    const seg = segments[i]
    if (i === 0) {
      // first segment must be submitter (the resolver requires this)
      states.push('user')
      continue
    }
    const allowed: TokenDef | undefined = TRANSITIONS[state].find(t => t.token === seg)
    if (!allowed) {
      states.push(state) // unknown — keep state, mark below
      descParts.push(`?${seg}`)
      continue
    }
    state = allowed.nextState
    states.push(state)
    descParts.push(allowed.zh)
  }
  return {
    segments,
    states,
    description: descParts.join(' → '),
    inWhitelist: (ACTOR_PATH_WHITELIST as readonly string[]).includes(path),
  }
}

interface Props {
  open: boolean
  initial: ActorPath | string
  onCancel: () => void
  onCommit: (path: string) => void
}

export function ActorPathBuilderModal({ open, initial, onCancel, onCommit }: Props) {
  const [path, setPath] = useState<string>(initial)
  useEffect(() => { if (open) setPath(initial) }, [open, initial])

  const walk = useMemo(() => walkPath(path), [path])

  function appendToken(t: TokenDef) {
    const next = path ? `${path}.${t.token}` : `submitter.${t.token}`
    setPath(next)
  }

  function popSegment(idx: number) {
    setPath(walk.segments.slice(0, idx).join('.'))
  }

  // Next allowed transitions = depend on state after the last segment;
  // empty path treated as "user" state (submitter implicit).
  const currentState: State = walk.states.length > 0 ? walk.states[walk.states.length - 1] : 'user'
  const nextOptions = TRANSITIONS[currentState]

  return (
    <Modal
      open={open}
      onClose={onCancel}
      title="路徑建構器 / Org-chart path"
      size="lg"
      footer={
        <>
          <div className="mr-auto text-xs text-ink-muted">
            {walk.inWhitelist
              ? <span className="text-good">✓ 路徑在 whitelist 內</span>
              : <span className="text-warn">⚠ 自訂路徑，後端可能拒收</span>}
          </div>
          <Button variant="ghost" onClick={onCancel}>Cancel</Button>
          <Button variant="primary" onClick={() => onCommit(path)}>Save</Button>
        </>
      }
    >
      <div className="grid h-full grid-cols-[1fr_320px] gap-0">
        {/* Left: builder + preview */}
        <div className="space-y-4 border-r border-rule p-4">
          <p className="text-xs text-ink-muted">
            從「提案人」出發，依序加 segment 走 org chart。每個 segment 的下拉只列出在當前狀態合法的選項，所以結構天然 valid。
          </p>

          {/* Segments row */}
          <div className="rounded-md border border-rule bg-slate-50 p-3">
            <p className="mb-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">路徑</p>
            <div className="flex flex-wrap items-center gap-1">
              {walk.segments.map((seg, i) => (
                <SegmentChip
                  key={i}
                  token={seg}
                  index={i}
                  onRemove={() => popSegment(i)}
                  isStart={i === 0}
                />
              ))}
              {/* Add-next dropdown */}
              <details className="relative">
                <summary className="inline-flex cursor-pointer items-center gap-1 rounded border border-dashed border-rule bg-white px-2 py-0.5 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary">
                  + 加 segment <ChevronDown className="h-3 w-3" />
                </summary>
                <div className="absolute left-0 top-full z-10 mt-1 w-48 rounded-md border border-rule bg-white shadow-md">
                  {nextOptions.map(opt => (
                    <button
                      key={opt.token}
                      type="button"
                      onClick={() => appendToken(opt)}
                      className="block w-full px-3 py-1.5 text-left text-xs hover:bg-slate-50"
                    >
                      <span className="font-mono text-ink">.{opt.token}</span>
                      <span className="ml-2 text-ink-muted">{opt.zh}</span>
                    </button>
                  ))}
                </div>
              </details>
            </div>
          </div>

          {/* Preview */}
          <div>
            <p className="mb-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">預覽</p>
            <code className="block rounded border border-rule bg-white px-3 py-2 font-mono text-sm text-ink">
              {path || '(empty)'}
            </code>
            <p className="mt-1 text-xs text-ink-muted">語意：{walk.description}</p>
          </div>
        </div>

        {/* Right: templates */}
        <div className="space-y-3 overflow-y-auto bg-slate-50/40 p-4">
          <p className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">常用範本 · templates</p>
          <p className="text-[11px] text-ink-muted">點任一範本直接覆蓋目前路徑。</p>
          <div className="space-y-1">
            {ACTOR_PATH_WHITELIST.map(p => {
              const desc = walkPath(p).description
              const isCurrent = p === path
              return (
                <button
                  key={p}
                  type="button"
                  onClick={() => setPath(p)}
                  className={cn(
                    'flex w-full flex-col items-start rounded border bg-white px-2 py-1.5 text-left transition-colors',
                    isCurrent ? 'border-primary bg-primary/5' : 'border-rule hover:border-primary/40',
                  )}
                >
                  <code className="font-mono text-[11px] text-ink">{p}</code>
                  <span className="text-[10.5px] text-ink-muted">{desc}</span>
                </button>
              )
            })}
          </div>
        </div>
      </div>
    </Modal>
  )
}

function SegmentChip({ token, index, onRemove, isStart }: {
  token: string
  index: number
  onRemove: () => void
  isStart: boolean
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded border px-2 py-0.5 font-mono text-[11px]',
        isStart ? 'border-primary/30 bg-primary/5 text-primary' : 'border-rule bg-white text-ink',
      )}
    >
      {index > 0 && <span className="text-ink-faint">.</span>}
      <span>{token}</span>
      {!isStart && (
        <button
          type="button"
          onClick={onRemove}
          className="text-ink-faint hover:text-danger"
          title="移除此 segment 及後續"
        >
          <X className="h-3 w-3" />
        </button>
      )}
    </span>
  )
}
