/**
 * StepApprovers (v2 ActorRef DSL) — pill row alignment with FORMS /
 * DECISIONS, friendly chip summaries, and a 「常見範本」strip that
 * one-clicks common patterns so the customer rarely needs to dive
 * into the recursive ActorRefEditor.
 */
import { useState } from 'react'
import { AlertCircle, CheckCircle2, Sparkles } from 'lucide-react'
import { cn } from '@/lib/cn'
import { ActorRefEditor, emptyActor } from '@/components/wizard/ActorRefEditor'
import { defaultApprovalActions, type Approval, type DraftSpec, type ActorRef } from '@/lib/onboarding'

export function StepApprovers({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const approvalNodes = draft.flow.nodes.filter(n => n.type === 'approval')
  const [activeId, setActiveId] = useState<string>(approvalNodes[0]?.id ?? '')

  if (approvalNodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        這個流程沒有 approval 節點，無需設定簽核規則。
      </div>
    )
  }

  const safeActiveId = approvalNodes.find(n => n.id === activeId)?.id ?? approvalNodes[0].id

  const upsert = (a: Approval) => {
    const others = draft.approvals.filter(x => x.id !== a.id)
    setDraft({ ...draft, approvals: [...others, a] })
  }

  const getOrCreate = (id: string): Approval =>
    draft.approvals.find(a => a.id === id) ?? { id, approver: emptyActor('expr'), actions: defaultApprovalActions() }

  const activeNode = approvalNodes.find(n => n.id === safeActiveId)!
  const activeApproval = getOrCreate(safeActiveId)

  return (
    <div className="flex flex-col gap-4">
      <p className="text-xs text-ink-muted">
        每個 approval 節點需要一個 ActorRef 指涉「誰簽」。優先用結構化選項；
        真的寫不下再用「自然語言描述」。
      </p>

      {/* Pill row — one per approval node */}
      {approvalNodes.length > 1 && (
        <div className="flex flex-wrap gap-1.5">
          {approvalNodes.map(node => {
            const a = getOrCreate(node.id)
            const summary = summarize(a.approver)
            const ok = approverConfigured(a.approver)
            const isActive = node.id === safeActiveId
            return (
              <button
                key={node.id}
                onClick={() => setActiveId(node.id)}
                className={cn(
                  'flex items-center gap-2 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors',
                  isActive
                    ? 'border-primary bg-primary/5 text-ink'
                    : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
                )}
              >
                {ok
                  ? <CheckCircle2 className={cn('h-3.5 w-3.5', isActive ? 'text-good' : 'text-good/70')} />
                  : <AlertCircle className={cn('h-3.5 w-3.5', isActive ? 'text-warn' : 'text-warn/70')} />}
                <span>{node.label}</span>
                <span className={cn('truncate font-mono text-[10px]', isActive ? 'text-ink-muted' : 'text-ink-faint')}>
                  {summary}
                </span>
              </button>
            )
          })}
        </div>
      )}

      {/* Active approval editor */}
      <div className="space-y-3 rounded-md border border-rule bg-white">
        <div className="flex items-baseline justify-between gap-3 border-b border-rule bg-slate-50 px-3 py-2">
          <div className="flex items-center gap-2">
            <span className="text-sm font-semibold text-ink">{activeNode.label}</span>
            <span className="font-mono text-[10px] text-ink-faint">{activeNode.id}</span>
          </div>
          <span
            className={cn(
              'rounded px-2 py-0.5 font-mono text-[11px]',
              approverConfigured(activeApproval.approver) ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700',
            )}
          >
            {summarize(activeApproval.approver)}
          </span>
        </div>

        <div className="space-y-3 p-3">
          <PresetStrip
            onPick={(a) => upsert({ ...activeApproval, approver: a })}
            activeSummary={summarize(activeApproval.approver)}
          />
          <ActorRefEditor
            value={activeApproval.approver}
            onChange={(next: ActorRef) => upsert({ ...activeApproval, approver: next })}
          />
        </div>
      </div>
    </div>
  )
}

/** True iff the actor would resolve to something at runtime (rough — no
 *  backend resolution; just「ref filled / collection has actors / etc.」). */
function approverConfigured(a: ActorRef): boolean {
  switch (a.type) {
    case 'expr':             return !!a.path
    case 'principal':        return !!a.ref
    case 'conditional':      return approverConfigured(a.then) && approverConfigured(a.else)
    case 'collection':       return a.actors.length > 0 && a.actors.every(approverConfigured)
    case 'natural_language': return !!a.text.trim()
  }
}

function summarize(a: ActorRef): string {
  switch (a.type) {
    case 'expr':             return a.path ? `路徑 ${a.path}` : '路徑（未設定）'
    case 'principal':        return a.ref ? `指定 ${a.ref}` : '指定（未選）'
    case 'conditional':      return `條件式 if ${a.condition.field} ${a.condition.op} ${String(a.condition.value)}`
    case 'collection':       return `${a.mode === 'any' ? '任' : '全'}簽 ${a.actors.length} 人${a.mode === 'any' ? ` (≥${a.min_approvals ?? 1})` : ''}`
    case 'natural_language': return a.text ? `📝 「${a.text.slice(0, 18)}${a.text.length > 18 ? '…' : ''}」` : '📝（未填）'
  }
}

interface PresetCard {
  label: string
  desc: string
  build: () => ActorRef
}

const PRESETS: PresetCard[] = [
  {
    label: '主管批',
    desc: '提案人的直屬主管',
    build: () => ({ type: 'expr', path: 'submitter.manager' }),
  },
  {
    label: '主管 → 大主管',
    desc: '直屬主管不在時找上一層',
    build: () => ({
      type: 'expr',
      path: 'submitter.manager',
      fallback: { text: '主管不在時請走主管的主管 (submitter.manager.manager)' },
    }),
  },
  {
    label: '部門主管批',
    desc: '提案人所屬部門 head',
    build: () => ({ type: 'expr', path: 'submitter.department.head' }),
  },
  {
    label: '指定 HR 角色',
    desc: 'HR 角色任一人',
    build: () => ({ type: 'principal', ref: 'role:HR' }),
  },
  {
    label: '條件分流：金額大走 CEO',
    desc: 'amount ≥ 100000 → CEO；否則主管',
    build: () => ({
      type: 'conditional',
      condition: { field: 'amount', op: '>=', value: 100000 },
      then: { type: 'principal', ref: 'role:CEO' },
      else: { type: 'expr', path: 'submitter.manager' },
    }),
  },
  {
    label: '會簽：主管 + 部門主管',
    desc: 'any 模式 — 任一人通過即可',
    build: () => ({
      type: 'collection',
      mode: 'any',
      min_approvals: 1,
      actors: [
        { type: 'expr', path: 'submitter.manager' },
        { type: 'expr', path: 'submitter.department.head' },
      ],
    }),
  },
]

function PresetStrip({ onPick, activeSummary }: { onPick: (a: ActorRef) => void; activeSummary: string }) {
  return (
    <div className="rounded-md border border-dashed border-rule bg-slate-50/40 p-2">
      <div className="mb-1.5 flex items-center gap-1.5">
        <Sparkles className="h-3.5 w-3.5 text-primary" />
        <p className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">常見範本 — 一鍵套用</p>
        <p className="ml-2 text-[10.5px] text-ink-faint">目前：{activeSummary}</p>
      </div>
      <div className="flex flex-wrap gap-1">
        {PRESETS.map((p, i) => (
          <button
            key={i}
            type="button"
            onClick={() => onPick(p.build())}
            title={p.desc}
            className="flex flex-col items-start rounded border border-rule bg-white px-2 py-1 text-left hover:border-primary"
          >
            <span className="text-[11px] font-medium text-ink">{p.label}</span>
            <span className="text-[10px] text-ink-faint">{p.desc}</span>
          </button>
        ))}
      </div>
    </div>
  )
}
