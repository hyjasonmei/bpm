import { useEffect, useRef, type ReactNode } from 'react'

import { useFlowLabel } from '@/hooks/useFlowRegistry'
import { getJwt } from '@/lib/apiFetch'
import { useBranding } from '@/lib/branding'
import { decodeJwt } from '@/lib/jwt'
import { type FormCode } from '@/lib/workflow'

interface Props {
  flowCode: FormCode
  flowVersion: number
  caseId: string
  children: ReactNode
}

/**
 * Document head + foot for the printed case sheet.
 *
 * Both blocks carry `print-only` — invisible on screen, revealed by the
 * `@media print` layer in index.css. Wrapping happens once in the shared
 * `/cases/:flowCode/:caseId` route, so every chef-cooked CaseDetail
 * inherits the printed chrome with no per-flow edit.
 *
 * The timestamp is written straight into the DOM on `beforeprint` rather
 * than through React state: Chrome snapshots the page as soon as the event
 * handlers return, which is before React would flush a state update — a
 * re-render could miss the paint.
 *
 * "目前狀態" deliberately does NOT appear here: it lives in the per-flow
 * API response the chef component fetches, and every flow already prints
 * its own 狀態 / Status card.
 */
export function CasePrintChrome({ flowCode, flowVersion, caseId, children }: Props) {
  const branding = useBranding()
  const systemName = branding.systemName ?? 'BPM System'
  // Admin's flow registry is the single source of truth for a flow's display
  // name (FORMS is only its compile-time fallback, and some of its zhLabels
  // are stale — VENDOR_EXPENSE reads "採購申請" there). Pass the case's own
  // version so a historical case prints the name it was filed under.
  const flowLabel = useFlowLabel()(flowCode, flowVersion)
  const printedBy = viewerName()
  const stamps = useRef<(HTMLSpanElement | null)[]>([])

  useEffect(() => {
    const sync = () => {
      const now = formatNow()
      for (const el of stamps.current) if (el) el.textContent = now
    }
    sync()
    window.addEventListener('beforeprint', sync)
    return () => window.removeEventListener('beforeprint', sync)
  }, [])

  return (
    <div className="print-doc">
      {/* The head / foot become table-header-group / table-footer-group in
          print so they repeat on every page — and those display types drop
          the element's own margin and padding. All spacing therefore lives
          on the inner div, which is a normal block inside the group. */}
      <header className="print-only">
        <div className="mb-5 border-b-2 border-ink pb-3">
          <div className="flex items-start justify-between gap-4">
            <div className="flex items-center gap-2">
              {branding.logoDataUri && (
                <img src={branding.logoDataUri} alt="" className="h-8 w-auto max-w-[140px] object-contain" />
              )}
              <span className="text-sm font-semibold text-ink">{systemName}</span>
            </div>
            <div className="text-right">
              <h1 className="text-lg font-bold text-ink">{flowLabel} 案件單</h1>
              <p className="font-mono text-[11px] text-ink-muted">{flowCode} V{flowVersion}</p>
            </div>
          </div>
          <p className="mt-2 text-[11px] text-ink-muted">
            案號 <span className="font-mono">{caseId}</span>
            {' · '}列印時間 <span ref={el => { stamps.current[0] = el }} />
            {' · '}列印人 {printedBy}
          </p>
        </div>
      </header>

      {children}

      <footer className="print-only">
        <div className="mt-5 border-t border-rule pt-2 text-[10px] text-ink-muted">
          本文件由 {systemName} 於 <span ref={el => { stamps.current[1] = el }} /> 由 {printedBy} 列印
        </div>
      </footer>
    </div>
  )
}

/** Printer identity = the JWT the page is already authenticated with. */
function viewerName(): string {
  const tok = getJwt()
  if (!tok) return '—'
  const d = decodeJwt(tok)
  return d?.full_name ?? d?.email ?? '—'
}

function formatNow(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}/${pad(d.getMonth() + 1)}/${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
