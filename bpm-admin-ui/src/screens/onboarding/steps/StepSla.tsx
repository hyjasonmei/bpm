import { Field, Input, Select, Checkbox } from '@/components/ui/form'
import type { DraftSpec, NodeSLA } from '@/lib/onboarding'

const ESCALATION_ACTIONS = ['notify', 'reassign', 'escalate_one_level', 'auto_approve', 'auto_reject'] as const

const DURATION_PRESETS = [
  { value: '4h',  label: '4h (半天)' },
  { value: '8h',  label: '8h (1 工作天)' },
  { value: '16h', label: '16h (2 工作天)' },
  { value: '24h', label: '24h' },
  { value: '48h', label: '48h' },
  { value: '72h', label: '72h (3 天)' },
]

export function StepSla({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  // SLA only meaningful for approval / userTask / serviceTask nodes
  const slaableNodes = draft.flow.nodes.filter(n =>
    n.type === 'approval' || n.type === 'userTask' || n.type === 'serviceTask'
  )

  if (slaableNodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        無 approval / userTask / serviceTask 節點，無需設定 SLA。
      </div>
    )
  }

  const upsert = (nodeId: string, sla: NodeSLA | null) => {
    const next = { ...draft.sla.perNode }
    if (sla === null) delete next[nodeId]
    else next[nodeId] = sla
    setDraft({ ...draft, sla: { perNode: next } })
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-muted max-w-xl">
        每個節點可設定處理時限與超時 escalation 行為。SLA 階段非阻擋（無設定也能繼續），
        但生 code 時會跳過 SLA / escalation 邏輯。
      </p>

      {slaableNodes.map(node => {
        const sla = draft.sla.perNode[node.id]
        const enabled = !!sla
        return (
          <div key={node.id} className="rounded-md border border-rule bg-white">
            <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-3 py-2">
              <div className="flex items-center gap-2">
                <span className="text-sm font-semibold text-ink">{node.label}</span>
                <span className="font-mono text-[10px] text-ink-faint">{node.id} · {node.type}</span>
              </div>
              <Checkbox
                id={`sla-${node.id}`}
                checked={enabled}
                onChange={e => upsert(node.id, e.target.checked
                  ? { duration: '8h', businessHoursOnly: true, escalation: { after: '8h', action: 'notify' } }
                  : null)}
                label="Enable SLA"
              />
            </div>

            {enabled && (
              <div className="space-y-3 p-3">
                <div className="grid grid-cols-2 gap-2">
                  <Field label="Duration" required hint="處理時限。常用 8h / 24h / 48h">
                    <Select
                      value={DURATION_PRESETS.some(d => d.value === sla.duration) ? sla.duration : '__custom__'}
                      onChange={e => {
                        const v = e.target.value
                        if (v === '__custom__') return
                        upsert(node.id, { ...sla, duration: v })
                      }}
                    >
                      {DURATION_PRESETS.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
                      <option value="__custom__">Custom…</option>
                    </Select>
                  </Field>
                  {!DURATION_PRESETS.some(d => d.value === sla.duration) && (
                    <Field label="Custom duration" hint="格式: 4h / 24h / 3d">
                      <Input
                        value={sla.duration}
                        onChange={e => upsert(node.id, { ...sla, duration: e.target.value })}
                      />
                    </Field>
                  )}
                  <Checkbox
                    id={`sla-${node.id}-bh`}
                    checked={!!sla.businessHoursOnly}
                    onChange={e => upsert(node.id, { ...sla, businessHoursOnly: e.target.checked })}
                    label="Business hours only (週一-五 9-18 才計時)"
                  />
                </div>

                <EscalationBlock
                  value={sla.escalation}
                  defaultAfter={sla.duration}
                  onChange={es => upsert(node.id, { ...sla, escalation: es })}
                />
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

function EscalationBlock({
  value, defaultAfter, onChange,
}: {
  value: NodeSLA['escalation']
  defaultAfter: string
  onChange: (es: NodeSLA['escalation']) => void
}) {
  if (!value) {
    return (
      <button
        onClick={() => onChange({ after: defaultAfter, action: 'notify' })}
        className="text-[11px] text-blue-600 hover:underline"
      >
        + Add escalation
      </button>
    )
  }
  return (
    <div className="space-y-2 rounded border border-rule bg-slate-50 p-2">
      <p className="text-xs font-semibold text-ink">Escalation</p>
      <div className="grid grid-cols-2 gap-2">
        <Field label="After" hint={`格式 4h / 50%（相對於 duration ${defaultAfter}）`}>
          <Input value={value.after} onChange={e => onChange({ ...value, after: e.target.value })} />
        </Field>
        <Field label="Action">
          <Select value={value.action} onChange={e => onChange({ ...value, action: e.target.value as NodeSLA['escalation'] extends infer _ ? 'notify' : never })}>
            {ESCALATION_ACTIONS.map(a => <option key={a} value={a}>{a}</option>)}
          </Select>
        </Field>
      </div>
      <button onClick={() => onChange(undefined)} className="text-[11px] text-danger hover:underline">
        Remove escalation
      </button>
    </div>
  )
}
