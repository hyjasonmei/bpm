import { Field, Input, Select, Checkbox } from '@/components/ui/form'
import type { DraftSpec, Decision, DecisionBranch } from '@/lib/onboarding'

export function StepDecisions({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const gateways = draft.flow.nodes.filter(n => n.type === 'gateway')

  if (gateways.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        這個流程沒有 gateway 節點，無需設定決策規則。
      </div>
    )
  }

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

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-muted">
        每個 gateway 都需要設定 type 跟每條 outgoing edge 的條件。Exclusive 模式恰好一條 isDefault。
      </p>

      {gateways.map(gw => {
        const decision = draft.decisions.find(d => d.id === gw.id) ?? getOrCreate(gw.id)
        const outgoing = draft.flow.edges.filter(e => e.source === gw.id)

        return (
          <div key={gw.id} className="rounded-md border border-rule bg-white">
            <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold text-ink">{gw.label}</span>
                <span className="font-mono text-[10px] text-ink-faint">{gw.id}</span>
              </div>
              <div className="flex items-center gap-2 text-[11px]">
                <span className={
                  decision.branches.length === outgoing.length
                    ? 'rounded bg-emerald-50 px-2 py-0.5 font-medium text-emerald-700'
                    : 'rounded bg-rose-50 px-2 py-0.5 font-medium text-rose-700'
                }>
                  {decision.branches.length}/{outgoing.length} branches
                </span>
                {decision.type === 'exclusive' && (
                  <span className={
                    decision.branches.filter(b => b.isDefault).length === 1
                      ? 'rounded bg-emerald-50 px-2 py-0.5 font-medium text-emerald-700'
                      : 'rounded bg-rose-50 px-2 py-0.5 font-medium text-rose-700'
                  }>
                    {decision.branches.filter(b => b.isDefault).length === 1 ? 'has default' : 'needs exactly 1 default'}
                  </span>
                )}
              </div>
            </div>

            <div className="p-3 space-y-3">
              <Field label="Decision type">
                <Select value={decision.type} onChange={e => upsert({ ...decision, type: e.target.value as Decision['type'] })}>
                  <option value="exclusive">exclusive — 走唯一一條符合的</option>
                  <option value="parallel">parallel — 全部都走</option>
                  <option value="inclusive">inclusive — 符合的都走</option>
                </Select>
              </Field>

              <div>
                <p className="mb-2 text-xs font-semibold text-ink">Branches</p>
                <div className="space-y-2">
                  {outgoing.map(edge => {
                    const branchIdx = decision.branches.findIndex(b => b.edgeId === edge.id)
                    const branch: DecisionBranch = branchIdx >= 0
                      ? decision.branches[branchIdx]
                      : { edgeId: edge.id, condition: '', isDefault: false }
                    const targetNode = draft.flow.nodes.find(n => n.id === edge.target)

                    const updateBranch = (b: DecisionBranch) => {
                      const others = decision.branches.filter(x => x.edgeId !== edge.id)
                      // exclusive: setting isDefault on this branch clears others
                      const cleared = b.isDefault && decision.type === 'exclusive'
                        ? others.map(x => ({ ...x, isDefault: false }))
                        : others
                      upsert({ ...decision, branches: [...cleared, b] })
                    }

                    return (
                      <div key={edge.id} className="grid grid-cols-[120px_1fr_140px] gap-2 items-end rounded border border-rule bg-slate-50 p-2.5">
                        <div>
                          <p className="text-[10px] uppercase tracking-wider text-ink-faint">Edge → target</p>
                          <p className="text-sm text-ink">
                            <span className="font-mono text-ink-muted">{edge.id}</span>
                            <span className="mx-1">→</span>
                            <span className="font-medium">{targetNode?.label ?? edge.target}</span>
                          </p>
                        </div>
                        <Field label={`Condition${decision.type === 'exclusive' ? '' : ' (parallel/inclusive 仍要寫，便於 BPMN 標註)'}`}>
                          <Input
                            placeholder="e.g. amount >= 10000"
                            value={branch.condition}
                            onChange={e => updateBranch({ ...branch, condition: e.target.value })}
                          />
                        </Field>
                        <Checkbox
                          id={`${decision.id}-${edge.id}-default`}
                          checked={!!branch.isDefault}
                          onChange={e => updateBranch({ ...branch, isDefault: e.target.checked })}
                          label="isDefault"
                        />
                      </div>
                    )
                  })}
                </div>
              </div>
            </div>
          </div>
        )
      })}
    </div>
  )
}
