import { BpmnDiagram } from '@/components/BpmnDiagram'
import type { DraftSpec } from '@/lib/onboarding'

export function StepStructure({ draft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  if (draft.flow.nodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint">
        尚未從 SOURCE step 載入流程。回上一步、選範本或上傳。
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <p className="text-xs text-ink-muted">
        bpmn-js 即時渲染流程拓撲。Phase A：拓撲只能從 preset / spec import 來、無法在這裡用滑鼠拉。
        要編輯節點 / 邊請回 SOURCE 改 preset，或在後續 step（FORMS / DECISIONS / APPROVERS）改該節點對應的設定。Phase B 會接 bpmn-js Modeler。
      </p>

      <BpmnDiagram draft={draft} height={420} />

      <details className="text-xs text-ink-muted">
        <summary className="cursor-pointer">節點明細 ({draft.flow.nodes.length}) / 邊明細 ({draft.flow.edges.length})</summary>
        <div className="mt-2 grid grid-cols-2 gap-3">
          <div>
            <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-muted">Nodes</p>
            <div className="rounded border border-rule bg-slate-50 p-2 font-mono text-[10px]">
              {draft.flow.nodes.map(n => (
                <div key={n.id}>
                  <span className="text-ink-faint">{n.type.padEnd(13)}</span>
                  <span className="text-ink">{n.label}</span>
                  <span className="ml-2 text-ink-faint">[{n.id}]</span>
                </div>
              ))}
            </div>
          </div>
          <div>
            <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-ink-muted">Edges</p>
            <div className="rounded border border-rule bg-slate-50 p-2 font-mono text-[10px]">
              {draft.flow.edges.map(e => (
                <div key={e.id}>
                  <span className="text-ink-faint">{e.id}</span>
                  <span className="ml-2 text-ink">{e.source} → {e.target}</span>
                  {e.condition && <span className="ml-2 text-amber-700">({e.condition})</span>}
                  {e.isDefault && <span className="ml-1 italic text-ink-faint">default</span>}
                </div>
              ))}
            </div>
          </div>
        </div>
      </details>
    </div>
  )
}
