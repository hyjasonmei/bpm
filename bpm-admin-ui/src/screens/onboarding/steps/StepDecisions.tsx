import { useState, type ReactNode } from 'react'
import { AlertCircle, CheckCircle2, Code2, Star } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Field, Select } from '@/components/ui/form'
import { ExpressionEditorModal } from '@/components/expression-editor/ExpressionEditorModal'
import type { DraftSpec, Decision, DecisionBranch } from '@/lib/onboarding'

export function StepDecisions({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const gateways = draft.flow.nodes.filter(n => n.type === 'gateway')
  const [activeGwId, setActiveGwId] = useState<string>(gateways[0]?.id ?? '')

  if (gateways.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        這個流程沒有 gateway 節點，無需設定決策規則。
      </div>
    )
  }

  // Ensure activeGwId stays valid even if SOURCE deletes the active node.
  const safeActiveId = gateways.find(g => g.id === activeGwId)?.id ?? gateways[0].id

  const upsert = (d: Decision) => {
    const others = draft.decisions.filter(x => x.id !== d.id)
    setDraft({ ...draft, decisions: [...others, d] })
  }

  const getOrCreate = (gatewayId: string): Decision => {
    const existing = draft.decisions.find(d => d.id === gatewayId)
    if (existing) return existing
    const outgoing = draft.flow.edges.filter(e => e.source === gatewayId)
    return {
      id: gatewayId,
      type: 'exclusive',
      branches: outgoing.map(e => ({ edgeId: e.id, condition: e.condition ?? '', isDefault: e.isDefault })),
    }
  }

  const variableNames = draft.variables.map(v => v.name).filter(Boolean)
  // Gateway conditions reference fields from any user task earlier in
  // the flow (譬如 leave_type, amount). Surface the union of all form
  // field IDs so the CEL editor can offer them as clickable refs.
  const formFieldIds = Array.from(new Set(
    draft.userTasks.flatMap(t => t.fields.map(f => f.id)).filter(Boolean)
  ))

  return (
    <div className="flex flex-col gap-4">
      <p className="text-xs text-ink-muted">
        每個 gateway 都需要設定 type 跟每條 outgoing edge 的條件。
        Exclusive 模式恰好一條 isDefault。
      </p>

      {/* Pill row — one per gateway */}
      {gateways.length > 1 && (
        <div className="flex flex-wrap gap-1.5">
          {gateways.map(gw => {
            const dec = draft.decisions.find(d => d.id === gw.id) ?? getOrCreate(gw.id)
            const outgoing = draft.flow.edges.filter(e => e.source === gw.id)
            const valid = decisionValid(dec, outgoing.length)
            const isActive = gw.id === safeActiveId
            return (
              <button
                key={gw.id}
                onClick={() => setActiveGwId(gw.id)}
                className={cn(
                  'flex items-center gap-2 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors',
                  isActive
                    ? 'border-primary bg-primary/5 text-ink'
                    : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
                )}
              >
                {valid
                  ? <CheckCircle2 className={cn('h-3.5 w-3.5', isActive ? 'text-good' : 'text-good/70')} />
                  : <AlertCircle className={cn('h-3.5 w-3.5', isActive ? 'text-warn' : 'text-warn/70')} />}
                <span>{gw.label}</span>
                <span className={cn(
                  'rounded-full px-1.5 text-[10px] font-mono',
                  isActive ? 'bg-white text-ink-muted' : 'bg-slate-100 text-ink-faint',
                )}>
                  {dec.branches.length}/{outgoing.length}
                </span>
              </button>
            )
          })}
        </div>
      )}

      <GatewayEditor
        gateway={gateways.find(g => g.id === safeActiveId)!}
        decision={draft.decisions.find(d => d.id === safeActiveId) ?? getOrCreate(safeActiveId)}
        outgoing={draft.flow.edges.filter(e => e.source === safeActiveId)}
        nodes={draft.flow.nodes}
        formFieldIds={formFieldIds}
        variableNames={variableNames}
        onChange={upsert}
      />
    </div>
  )
}

function decisionValid(decision: Decision, expectedBranches: number): boolean {
  if (decision.branches.length !== expectedBranches) return false
  if (decision.type === 'exclusive') {
    return decision.branches.filter(b => b.isDefault).length === 1
  }
  return true
}

function GatewayEditor({
  gateway, decision, outgoing, nodes, formFieldIds, variableNames, onChange,
}: {
  gateway: { id: string; label: string }
  decision: Decision
  outgoing: { id: string; target: string }[]
  nodes: { id: string; label: string }[]
  formFieldIds: string[]
  variableNames: string[]
  onChange: (d: Decision) => void
}) {
  const hasDefault = decision.branches.filter(b => b.isDefault).length === 1
  const branchCount = decision.branches.length
  const expected = outgoing.length

  return (
    <div className="rounded-md border border-rule bg-white">
      <div className="flex items-center justify-between gap-3 border-b border-rule bg-slate-50 px-3 py-2">
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-ink">{gateway.label}</span>
          <span className="font-mono text-[10px] text-ink-faint">{gateway.id}</span>
        </div>
        <div className="flex items-center gap-2 text-[11px]">
          <span className={cn(
            'rounded px-2 py-0.5 font-medium',
            branchCount === expected ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-50 text-rose-700',
          )}>
            {branchCount}/{expected} branches
          </span>
          {decision.type === 'exclusive' && (
            <span className={cn(
              'rounded px-2 py-0.5 font-medium',
              hasDefault ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-50 text-rose-700',
            )}>
              {hasDefault ? '✓ default 已指定' : '⚠ 缺 default branch'}
            </span>
          )}
        </div>
      </div>

      <div className="space-y-3 p-3">
        <Field label="Decision type" hint="exclusive 最常用；parallel = 同時全走 / 並簽；inclusive = 條件吻合的都走 (罕見)">
          <Select value={decision.type} onChange={e => onChange({ ...decision, type: e.target.value as Decision['type'] })}>
            <option value="exclusive">exclusive — 擇一走（最常用）</option>
            <option value="parallel">parallel — 同時全走 / 並簽（多人同時簽核）</option>
            <option value="inclusive">inclusive — 條件吻合的都走（罕見）</option>
          </Select>
        </Field>

        {decision.type === 'parallel' && (
          <ParallelJoinConfig
            branchCount={outgoing.length}
            threshold={decision.joinThreshold}
            branchLabels={outgoing.map(e => nodes.find(n => n.id === e.target)?.label ?? e.target)}
            onChange={t => onChange({ ...decision, joinThreshold: t })}
          />
        )}

        <div>
          <p className="mb-2 text-xs font-semibold text-ink">Branches</p>
          <div className="space-y-2">
            {[...outgoing]
              // Show the default branch first when in exclusive mode.
              .sort((a, b) => {
                if (decision.type !== 'exclusive') return 0
                const ad = decision.branches.find(x => x.edgeId === a.id)?.isDefault ?? false
                const bd = decision.branches.find(x => x.edgeId === b.id)?.isDefault ?? false
                return (bd ? 1 : 0) - (ad ? 1 : 0)
              })
              .map(edge => {
                const existing = decision.branches.find(b => b.edgeId === edge.id)
                const branch: DecisionBranch = existing ?? { edgeId: edge.id, condition: '', isDefault: false }
                const targetNode = nodes.find(n => n.id === edge.target)

                const updateBranch = (next: DecisionBranch) => {
                  const others = decision.branches.filter(x => x.edgeId !== edge.id)
                  // exclusive: setting isDefault here clears others
                  const cleared = next.isDefault && decision.type === 'exclusive'
                    ? others.map(x => ({ ...x, isDefault: false }))
                    : others
                  onChange({ ...decision, branches: [...cleared, next] })
                }

                return (
                  <BranchRow
                    key={edge.id}
                    edgeId={edge.id}
                    targetLabel={targetNode?.label ?? edge.target}
                    branch={branch}
                    decisionType={decision.type}
                    contextFieldIds={formFieldIds}
                    variableNames={variableNames}
                    onChange={updateBranch}
                  />
                )
              })}
          </div>
        </div>
      </div>
    </div>
  )
}

function ParallelJoinConfig({ branchCount, threshold, branchLabels, onChange }: {
  branchCount: number
  threshold?: number
  branchLabels: string[]
  onChange: (t: number | undefined) => void
}) {
  const mode = threshold == null ? 'all' : threshold <= 1 ? 'any' : 'threshold'
  const clamp = (n: number) => Math.max(1, Math.min(branchCount, n))
  const Btn = ({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) => (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md border px-3 py-1.5 text-xs font-medium transition-colors',
        active ? 'border-primary bg-primary/10 text-ink' : 'border-rule bg-white text-ink-muted hover:border-primary/40',
      )}
    >
      {children}
    </button>
  )
  return (
    <div className="space-y-2 rounded-md border border-primary/30 bg-primary/5 p-3">
      <p className="text-xs font-semibold text-ink">並簽 join 條件 — 幾個分支核准才通過</p>
      <div className="flex flex-wrap gap-2">
        <Btn active={mode === 'all'} onClick={() => onChange(undefined)}>全部核准（{branchCount}/{branchCount}）</Btn>
        <Btn active={mode === 'any'} onClick={() => onChange(1)}>任一核准（1/{branchCount}）</Btn>
        <Btn active={mode === 'threshold'} onClick={() => onChange(clamp(2))}>門檻 M/{branchCount}</Btn>
      </div>
      {mode === 'threshold' && (
        <label className="flex items-center gap-1 text-xs text-ink">
          需
          <input
            type="number"
            min={1}
            max={branchCount}
            value={threshold ?? 2}
            onChange={e => onChange(clamp(Number(e.target.value) || 1))}
            className="w-14 rounded border border-rule px-2 py-1 text-sm"
          />
          / {branchCount} 個分支核准
        </label>
      )}
      <p className="text-[11px] text-ink-muted">
        並行分支：{branchLabels.join('、')}。任一分支退件 → 整關立刻退件。
      </p>
    </div>
  )
}

function BranchRow({
  edgeId, targetLabel, branch, decisionType, contextFieldIds, variableNames, onChange,
}: {
  edgeId: string
  targetLabel: string
  branch: DecisionBranch
  decisionType: Decision['type']
  contextFieldIds: string[]
  variableNames: string[]
  onChange: (next: DecisionBranch) => void
}) {
  const [exprOpen, setExprOpen] = useState(false)
  const isDefault = !!branch.isDefault
  const empty = !branch.condition.trim()
  const isExclusive = decisionType === 'exclusive'

  return (
    <div
      className={cn(
        'rounded border bg-slate-50 p-2.5',
        isDefault && isExclusive ? 'border-primary/40 bg-primary/5' : 'border-rule',
      )}
    >
      <div className="mb-2 flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          {isDefault && isExclusive && (
            <span className="inline-flex items-center gap-1 rounded-full bg-primary px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-white">
              <Star className="h-3 w-3" />
              Default
            </span>
          )}
          <p className="text-sm text-ink">
            <span className="font-mono text-[11px] text-ink-muted">{edgeId}</span>
            <span className="mx-1.5 text-ink-faint">→</span>
            <span className="font-medium">{targetLabel}</span>
          </p>
        </div>
        {isExclusive && (
          <button
            type="button"
            onClick={() => onChange({ ...branch, isDefault: !isDefault })}
            className={cn(
              'rounded border px-2 py-0.5 text-[11px] font-medium transition-colors',
              isDefault
                ? 'border-primary bg-white text-primary hover:bg-primary/5'
                : 'border-rule bg-white text-ink-muted hover:border-primary hover:text-primary',
            )}
            title="exclusive 模式下，預設 branch 是條件都不符合時走的那條"
          >
            {isDefault ? '取消 default' : '設為 default'}
          </button>
        )}
      </div>

      <div>
        <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          條件 / Condition {decisionType !== 'exclusive' && '— parallel/inclusive 仍要寫，便於 BPMN 標註'}
        </span>
        <button
          type="button"
          onClick={() => setExprOpen(true)}
          className={cn(
            'group flex w-full items-center gap-2 rounded border bg-white px-2 py-1 text-left hover:border-primary',
            empty ? 'border-dashed border-rule' : 'border-rule',
          )}
        >
          <Code2 className={cn('h-3.5 w-3.5 shrink-0', empty ? 'text-ink-faint' : 'text-primary')} />
          <code className={cn('flex-1 truncate font-mono text-[11px]', empty ? 'text-ink-faint' : 'text-ink')}>
            {empty
              ? (isDefault && isExclusive ? 'default branch — 條件可留空' : '點此設定條件…')
              : branch.condition}
          </code>
          <span className="text-[10px] text-ink-faint group-hover:text-primary">編輯 →</span>
        </button>
      </div>

      <ExpressionEditorModal
        open={exprOpen}
        title={`條件 — ${edgeId} → ${targetLabel}`}
        shape="boolean"
        initial={branch.condition}
        placeholder="amount >= 10000"
        contextFieldIds={contextFieldIds}
        contextVariables={variableNames}
        onCancel={() => setExprOpen(false)}
        onCommit={(v) => { onChange({ ...branch, condition: v }); setExprOpen(false) }}
      />
    </div>
  )
}
