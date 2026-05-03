import { Field, Input, Select } from '@/components/ui/form'
import type { DraftSpec, Approval, ApprovalRule } from '@/lib/onboarding'

const RULE_TYPES: ApprovalRule['type'][] = ['direct_manager', 'role', 'specific_user', 'department_head']

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
    draft.approvals.find(a => a.id === id) ?? { id, rule: { type: 'direct_manager' } }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-muted">
        每個 approval 節點需要設定誰來簽。Fallback 規則用於主規則找不到人時的補位。
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
                {approval.rule.type}{approval.fallback && ` → fallback:${approval.fallback.type}`}
              </span>
            </div>

            <div className="p-3 space-y-4">
              <RuleEditor
                title="主規則 / Primary rule"
                value={approval.rule}
                onChange={r => upsert({ ...approval, rule: r })}
              />
              <FallbackBlock
                value={approval.fallback}
                onChange={f => upsert({ ...approval, fallback: f })}
              />
            </div>
          </div>
        )
      })}
    </div>
  )
}

function RuleEditor({ title, value, onChange }: { title: string; value: ApprovalRule; onChange: (r: ApprovalRule) => void }) {
  return (
    <div className="rounded border border-rule bg-slate-50 p-3 space-y-2">
      <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">{title}</p>
      <div className="grid grid-cols-2 gap-2">
        <Field label="Type">
          <Select
            value={value.type}
            onChange={e => onChange(emptyRule(e.target.value as ApprovalRule['type']))}
          >
            {RULE_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
          </Select>
        </Field>
        {value.type === 'role' && (
          <Field label="Role" hint="如 Finance / CEO / VP / HR">
            <Input
              value={(value as { type: 'role'; role: string }).role ?? ''}
              onChange={e => onChange({ type: 'role', role: e.target.value })}
              placeholder="Finance"
            />
          </Field>
        )}
        {value.type === 'specific_user' && (
          <Field label="User ID" hint="employee id, 例 u_chen_vp">
            <Input
              value={(value as { type: 'specific_user'; userId: string }).userId ?? ''}
              onChange={e => onChange({ type: 'specific_user', userId: e.target.value })}
              placeholder="u_..."
            />
          </Field>
        )}
        {value.type === 'department_head' && (
          <Field label="Department of">
            <Select
              value={(value as { type: 'department_head'; deptOf: 'applicant' }).deptOf ?? 'applicant'}
              onChange={e => onChange({ type: 'department_head', deptOf: e.target.value as 'applicant' })}
            >
              <option value="applicant">applicant</option>
            </Select>
          </Field>
        )}
        {value.type === 'direct_manager' && (
          <div className="self-end text-[11px] text-ink-faint italic">
            無額外設定 — 自動找申請人的 direct manager
          </div>
        )}
      </div>
    </div>
  )
}

function FallbackBlock({ value, onChange }: { value: ApprovalRule | undefined; onChange: (r: ApprovalRule | undefined) => void }) {
  if (!value) {
    return (
      <button
        onClick={() => onChange({ type: 'role', role: '' })}
        className="text-[11px] text-blue-600 hover:underline"
      >
        + Add fallback rule
      </button>
    )
  }
  return (
    <div className="space-y-2">
      <RuleEditor title="Fallback (主規則找不到人時用)" value={value} onChange={onChange} />
      <button onClick={() => onChange(undefined)} className="text-[11px] text-danger hover:underline">
        Remove fallback
      </button>
    </div>
  )
}

function emptyRule(type: ApprovalRule['type']): ApprovalRule {
  switch (type) {
    case 'direct_manager': return { type: 'direct_manager' }
    case 'role': return { type: 'role', role: '' }
    case 'specific_user': return { type: 'specific_user', userId: '' }
    case 'department_head': return { type: 'department_head', deptOf: 'applicant' }
  }
}
