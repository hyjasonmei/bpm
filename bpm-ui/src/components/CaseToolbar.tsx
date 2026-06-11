import { useState } from 'react'
import { Link2, Printer, Check } from 'lucide-react'
import { Button } from '@/components/ui/button'

/**
 * Read-only actions for a case-detail page — copy a permanent link to the
 * case + print. Rendered by the shared `FeatureCaseDetailRoute` wrapper,
 * absolutely positioned to sit just LEFT of the per-flow "View BPMN"
 * button, so every chef-cooked CaseDetail gets these without editing
 * feature code.
 *
 * Icon-only with native tooltips. Tagged `no-print` so it disappears in
 * the print view (see the `@media print` block in `index.css`).
 */
export function CaseToolbar() {
  const [copied, setCopied] = useState(false)

  async function copyLink() {
    const url = window.location.href
    try {
      await navigator.clipboard.writeText(url)
    } catch {
      // Clipboard API can be blocked (insecure context / permissions);
      // fall back to a hidden textarea + execCommand so copy still works.
      const ta = document.createElement('textarea')
      ta.value = url
      ta.style.position = 'fixed'
      ta.style.opacity = '0'
      document.body.appendChild(ta)
      ta.select()
      try { document.execCommand('copy') } catch { /* give up silently */ }
      ta.remove()
    }
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1500)
  }

  return (
    <div className="pointer-events-auto no-print flex items-center gap-1.5">
      <Button
        variant="outline"
        size="icon"
        onClick={() => { void copyLink() }}
        title={copied ? '已複製連結' : '複製本案連結'}
        aria-label="複製本案連結"
      >
        {copied
          ? <Check className="h-3.5 w-3.5 text-emerald-600" />
          : <Link2 className="h-3.5 w-3.5" />}
      </Button>
      <Button
        variant="outline"
        size="icon"
        onClick={() => window.print()}
        title="列印此案件"
        aria-label="列印此案件"
      >
        <Printer className="h-3.5 w-3.5" />
      </Button>
    </div>
  )
}
