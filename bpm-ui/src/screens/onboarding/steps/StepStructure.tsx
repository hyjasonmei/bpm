import { ArrowRight } from 'lucide-react'
import type { DraftSpec, FlowNode } from '@/lib/onboarding'

const NODE_STYLES: Record<FlowNode['type'], { bg: string; ring: string; label: string }> = {
  startEvent:  { bg: 'bg-emerald-100', ring: 'ring-emerald-300', label: 'Start' },
  endEvent:    { bg: 'bg-rose-100',    ring: 'ring-rose-300',    label: 'End' },
  userTask:    { bg: 'bg-blue-100',    ring: 'ring-blue-300',    label: 'User Task' },
  approval:    { bg: 'bg-amber-100',   ring: 'ring-amber-300',   label: 'Approval' },
  gateway:     { bg: 'bg-violet-100',  ring: 'ring-violet-300',  label: 'Gateway' },
  serviceTask: { bg: 'bg-cyan-100',    ring: 'ring-cyan-300',    label: 'Service' },
  notify:      { bg: 'bg-yellow-100',  ring: 'ring-yellow-300',  label: 'Notify' },
}

export function StepStructure({ draft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  if (draft.flow.nodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        尚未從 SOURCE step 載入流程。回上一步、選範本或上傳。
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-5">
      <section>
        <h3 className="mb-3 text-sm font-semibold text-ink">節點 ({draft.flow.nodes.length})</h3>
        <div className="flex flex-wrap items-center gap-2">
          {draft.flow.nodes.map((n, i) => {
            const s = NODE_STYLES[n.type]
            return (
              <div key={n.id} className="flex items-center gap-2">
                <div className={`flex flex-col items-center justify-center rounded-md ${s.bg} ${s.ring} ring-1 px-3 py-2 min-w-[100px]`}>
                  <span className="font-mono text-[9px] uppercase tracking-wider text-ink-muted">{s.label}</span>
                  <span className="text-xs font-semibold text-ink">{n.label}</span>
                </div>
                {i < draft.flow.nodes.length - 1 && <ArrowRight className="h-4 w-4 text-slate-400" />}
              </div>
            )
          })}
        </div>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold text-ink">邊 ({draft.flow.edges.length})</h3>
        <div className="overflow-hidden rounded border border-rule">
          <table className="w-full text-xs">
            <thead className="bg-slate-50">
              <tr className="text-left text-[10px] uppercase tracking-wider text-ink-muted">
                <th className="px-3 py-2">ID</th>
                <th className="px-3 py-2">From</th>
                <th className="px-3 py-2">To</th>
                <th className="px-3 py-2">Condition</th>
              </tr>
            </thead>
            <tbody>
              {draft.flow.edges.map(e => (
                <tr key={e.id} className="border-t border-rule">
                  <td className="px-3 py-1.5 font-mono text-ink-muted">{e.id}</td>
                  <td className="px-3 py-1.5 font-mono text-ink">{e.source}</td>
                  <td className="px-3 py-1.5 font-mono text-ink">{e.target}</td>
                  <td className="px-3 py-1.5 text-ink-muted">
                    {e.condition || (e.isDefault ? <span className="italic text-ink-faint">default</span> : '—')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <p className="rounded border border-amber-200 bg-amber-50 p-2.5 text-[11px] text-amber-800">
        Phase A：拓撲只能從 preset / spec import 來、無法在這裡編輯。Phase B 會接 bpmn-js 編輯器。
      </p>
    </div>
  )
}
