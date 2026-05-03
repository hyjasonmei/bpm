import { useEffect, useRef, useState } from 'react'
import BpmnViewer from 'bpmn-js/lib/NavigatedViewer'
import 'bpmn-js/dist/assets/diagram-js.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css'
import 'bpmn-js/dist/assets/bpmn-js.css'

import type { DraftSpec } from '@/lib/onboarding'
import { flowToBpmnXml } from '@/lib/bpmnXml'

interface Props {
  draft: DraftSpec
  height?: number
}

/**
 * Read-only bpmn-js viewer. The DraftSpec is the authoritative model;
 * editing is done via STRUCTURE / FORMS / DECISIONS / APPROVERS step UIs
 * (which mutate the typed model). bpmn-js just renders the diagram.
 */
export function BpmnDiagram({ draft, height = 360 }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const viewerRef = useRef<BpmnViewer | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!containerRef.current) return

    const viewer = new BpmnViewer({ container: containerRef.current })
    viewerRef.current = viewer

    return () => {
      viewer.destroy()
      viewerRef.current = null
    }
  }, [])

  useEffect(() => {
    const viewer = viewerRef.current
    if (!viewer || draft.flow.nodes.length === 0) {
      setError(null)
      return
    }
    const xml = flowToBpmnXml(draft)
    let cancelled = false
    viewer.importXML(xml).then(() => {
      if (cancelled) return
      // fit-viewport so even small flows render at a sensible zoom
      const canvas = viewer.get('canvas') as { zoom: (mode: string | number, center?: 'auto') => void }
      try { canvas.zoom('fit-viewport', 'auto') } catch { /* ignore */ }
      setError(null)
    }).catch((e: unknown) => {
      if (cancelled) return
      const msg = e instanceof Error ? e.message : String(e)
      setError(msg)
    })
    return () => { cancelled = true }
  }, [draft])

  if (draft.flow.nodes.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-10 text-center text-sm text-ink-faint" style={{ height }}>
        尚未從 SOURCE step 載入流程。回上一步、選範本或上傳。
      </div>
    )
  }

  return (
    <div className="relative">
      {error && (
        <div className="absolute left-2 top-2 z-10 rounded border border-amber-300 bg-amber-50 px-2 py-1 text-[11px] text-amber-800">
          BPMN render error: {error}
        </div>
      )}
      <div
        ref={containerRef}
        className="w-full rounded border border-rule bg-card"
        style={{ height }}
      />
    </div>
  )
}
