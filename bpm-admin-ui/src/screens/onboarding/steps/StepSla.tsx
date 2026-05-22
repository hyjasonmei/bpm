/**
 * StepSla (v2 simplification) — drop the relative "50%" escalation
 * syntax, lock both duration and escalation.after to absolute hours,
 * remove the Custom… escape hatch, group the escalation actions by
 * safety, and add a NoteRow per node for chef when structured rules
 * can't express the case.
 */
import { useState } from 'react'
import { Pencil, ShieldAlert, StickyNote } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Field, Select, Checkbox } from '@/components/ui/form'
import { NoteEditorModal } from '@/components/note-editor/NoteEditorModal'
import type { DraftSpec, NodeSLA } from '@/lib/onboarding'

// Hour presets — covers 1h up to 5 工作天 (40h). Beyond that, chef
// can read the `note` rather than letting the user type whatever.
const DURATION_PRESETS = [
  { value: '1h',  label: '1h' },
  { value: '2h',  label: '2h' },
  { value: '4h',  label: '4h (半天)' },
  { value: '8h',  label: '8h (1 工作天)' },
  { value: '16h', label: '16h (2 工作天)' },
  { value: '24h', label: '24h' },
  { value: '48h', label: '48h' },
  { value: '72h', label: '72h (3 天)' },
]

interface ActionDef {
  value: NonNullable<NodeSLA['escalation']>['action']
  label: string
  desc: string
}
const SAFE_ACTIONS: ActionDef[] = [
  { value: 'notify',             label: '通知',          desc: '寄信 / push 提醒 — 不改 task 狀態' },
  { value: 'reassign',           label: '重派',          desc: '系統把任務轉給 backup（同 role / 主管）' },
  { value: 'escalate_one_level', label: '往上一階',      desc: '改派給主管的主管' },
]
const RISKY_ACTIONS: ActionDef[] = [
  { value: 'auto_approve', label: '自動核准', desc: '⚠ 沒人處理就過 — 用在「小金額自動過」' },
  { value: 'auto_reject',  label: '自動駁回', desc: '⚠ 沒人處理就駁 — 用在「逾期視為棄權」' },
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
        但生 code 時會跳過 SLA / escalation 邏輯。本步驟全用絕對小時數。
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
                  <Field label="Duration" required hint="處理時限（小時）">
                    <Select
                      value={DURATION_PRESETS.some(d => d.value === sla.duration) ? sla.duration : '8h'}
                      onChange={e => upsert(node.id, { ...sla, duration: e.target.value })}
                    >
                      {DURATION_PRESETS.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
                    </Select>
                  </Field>
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

                <NoteBlock
                  value={sla.note}
                  onChange={n => upsert(node.id, { ...sla, note: n })}
                  nodeLabel={node.label}
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
  const isRisky = RISKY_ACTIONS.some(a => a.value === value.action)
  return (
    <div className="space-y-2 rounded border border-rule bg-slate-50 p-2">
      <p className="text-xs font-semibold text-ink">Escalation</p>
      <div className="grid grid-cols-2 gap-2">
        <Field label="超時多久後觸發" hint="絕對小時數，跟 Duration 同單位">
          <Select
            value={DURATION_PRESETS.some(d => d.value === value.after) ? value.after : '8h'}
            onChange={e => onChange({ ...value, after: e.target.value })}
          >
            {DURATION_PRESETS.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
          </Select>
        </Field>
        <Field label="動作" hint={isRisky ? '⚠ 此動作沒人處理會直接套用，請確認業務允許' : undefined}>
          <Select
            value={value.action}
            onChange={e => onChange({ ...value, action: e.target.value as NonNullable<NodeSLA['escalation']>['action'] })}
            className={cn(isRisky && 'border-warn focus:ring-warn/30')}
          >
            <optgroup label="安全（不直接終結 task）">
              {SAFE_ACTIONS.map(a => (
                <option key={a.value} value={a.value}>{a.label}</option>
              ))}
            </optgroup>
            <optgroup label="自動處理（⚠ 謹慎用）">
              {RISKY_ACTIONS.map(a => (
                <option key={a.value} value={a.value}>{a.label}</option>
              ))}
            </optgroup>
          </Select>
        </Field>
      </div>
      <p className="text-[10.5px] text-ink-muted">
        {[...SAFE_ACTIONS, ...RISKY_ACTIONS].find(a => a.value === value.action)?.desc}
      </p>
      {isRisky && (
        <p className="rounded border border-warn/30 bg-warn/5 px-2 py-1 text-[10.5px] text-ink">
          <ShieldAlert className="mr-1 inline h-3 w-3 text-warn" />
          自動處理：超時系統直接代為核准 / 駁回，沒人 review。確認業務允許再用。
        </p>
      )}
      <button onClick={() => onChange(undefined)} className="text-[11px] text-danger hover:underline">
        Remove escalation
      </button>
    </div>
  )
}

function NoteBlock({
  value, onChange, nodeLabel,
}: {
  value: NodeSLA['note']
  onChange: (n: NodeSLA['note']) => void
  nodeLabel: string
}) {
  const [open, setOpen] = useState(false)
  const text = value?.['zh-TW'] ?? ''
  const empty = !text.trim()
  return (
    <div>
      <p className="mb-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        備註 / NOTE — 給 chef 的自然語言補充
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
          {empty ? '點此寫給 chef…（譬如「夜班的 SLA 算法不同」、「跨時區員工放寬 2 小時」這種結構化寫不下的規則）' : text}
        </span>
        <span className="mt-0.5 inline-flex items-center gap-0.5 text-[10px] text-ink-faint group-hover:text-primary">
          <Pencil className="h-3 w-3" /> 編輯
        </span>
      </button>
      <NoteEditorModal
        open={open}
        title={`備註 — ${nodeLabel} 的 SLA`}
        initial={text}
        helper={
          <>
            上面的 Duration / Escalation 表達不下時用這欄。chef 會讀這段並寫對應的 timer / catch-all 邏輯。<br />
            一般使用者看不到此內容。
          </>
        }
        onCancel={() => setOpen(false)}
        onCommit={v => {
          onChange(v ? { 'zh-TW': v, ...(value?.en ? { en: value.en } : {}) } : undefined)
          setOpen(false)
        }}
      />
    </div>
  )
}
