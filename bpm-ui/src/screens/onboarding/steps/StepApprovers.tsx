import { ActorRefEditor, emptyActor } from '@/components/wizard/ActorRefEditor'
import type { Approval, DraftSpec, ActorRef } from '@/lib/onboarding'

/// StepApprovers (v1.1) — every approval node carries one ActorRef.
/// The DSL covers what the old ApprovalRule + fallback + requiresAll did,
/// plus conditional routing and any/all collection. See spec_schema.md §2.10.
export function StepApprovers({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const approvalNodes = draft.flow.nodes.filter(n => n.type === 'approval')

  if (approvalNodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        這個流程沒有 approval 節點，無需設定簽核規則。
      </div>
    )
  }

  const upsert = (a: Approval) => {
    const others = draft.approvals.filter(x => x.id !== a.id)
    setDraft({ ...draft, approvals: [...others, a] })
  }

  const getOrCreate = (id: string): Approval =>
    draft.approvals.find(a => a.id === id) ?? { id, approver: emptyActor('expr') }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-muted">
        每個 approval 節點需要一個 <code className="rounded bg-slate-100 px-1 py-0.5 text-[11px]">ActorRef</code>{' '}
        指涉「誰簽」。可以是：路徑（如 submitter.manager）、角色、群組、條件式、合議。Fallback 寫在 ActorRef 內。
      </p>

      {approvalNodes.map(node => {
        const approval = getOrCreate(node.id)
        return (
          <div key={node.id} className="rounded-md border border-rule bg-white">
            <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold text-ink">{node.label}</span>
                <span className="font-mono text-[10px] text-ink-faint">{node.id}</span>
              </div>
              <span className="rounded bg-blue-50 px-2 py-0.5 text-[11px] font-medium text-blue-700">
                {summarize(approval.approver)}
              </span>
            </div>

            <div className="p-3">
              <ActorRefEditor
                value={approval.approver}
                onChange={(next: ActorRef) => upsert({ ...approval, approver: next })}
              />
            </div>
          </div>
        )
      })}
    </div>
  )
}

function summarize(a: ActorRef): string {
  switch (a.type) {
    case 'expr':        return `expr:${a.path}`
    case 'role':        return `role:${a.code || '(unset)'}`
    case 'group':       return `group:${a.id?.slice(0, 8) || '(unset)'}`
    case 'user':        return `user:${a.id?.slice(0, 8) || '(unset)'}`
    case 'conditional': return `if ${a.condition.field}${a.condition.op}${String(a.condition.value)}`
    case 'collection':  return `${a.mode}(${a.actors.length}${a.mode === 'any' ? `, ≥${a.min_approvals ?? 1}` : ''})`
  }
}
