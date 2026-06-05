import { useEffect, useRef, useState } from 'react'
import BpmnViewer from 'bpmn-js/lib/NavigatedViewer'
import 'bpmn-js/dist/assets/diagram-js.css'
import 'bpmn-js/dist/assets/bpmn-font/css/bpmn.css'
import 'bpmn-js/dist/assets/bpmn-js.css'

interface Props {
  xml: string
  height?: number
}

/**
 * Read-only bpmn-js viewer driven by a raw BPMN XML string (no DraftSpec).
 * Used by the SOURCE step for flows registered from shipped chef code, whose
 * canonical BPMN lives in <c>Flow.BpmnXml</c> (the bundle's bpmn.xml) rather
 * than an AI-Kitchen spec. Pan + scroll-zoom only; no editing/palette.
 */
export function BpmnXmlView({ xml, height = 460 }: Props) {
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
    if (!viewer || !xml) return
    let cancelled = false
    viewer.importXML(xml).then(() => {
      if (cancelled) return
      const canvas = viewer.get('canvas') as { zoom: (mode: string | number, center?: 'auto') => void }
      try { canvas.zoom('fit-viewport', 'auto') } catch { /* ignore */ }
      setError(null)
    }).catch((e: unknown) => {
      if (cancelled) return
      setError(e instanceof Error ? e.message : String(e))
    })
    return () => { cancelled = true }
  }, [xml])

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
