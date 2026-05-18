import { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, ChefHat, RefreshCw, Sparkles } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import type { AdminScreen } from '@/components/AdminLayout'
import { apiFetch, getJwt, setJwt } from '@/lib/apiFetch'
import { type FlowState, type FlowSummary, listFlows } from '@/flowcook/api/flows'

type Mode = 'list' | 'wizard'

/**
 * AI Kitchen — entry page for the flow-design experience.
 *
 * Phase A: list + Cook new flow CTA + legacy wizard mount.
 * Phase B1 (this commit): list now fetches GET /api/flows from
 * bpm-admin-svc lifecycle BE. Wizard still backs to localStorage —
 * wiring the wizard to draft persistence is Phase B2.
 */
export function AiKitchenPage() {
  const [mode, setMode] = useState<Mode>('list')

  useEffect(() => {
    if (mode !== 'wizard') return
    let cancelled = false
    async function ensureBpmSvcJwt() {
      if (getJwt()) return
      try {
        const res = await apiFetch('/api/dev/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ PersonaCode: 'admin' }),
        })
        if (res.ok && !cancelled) {
          const data = await res.json()
          setJwt(data.token)
        }
      } catch { /* swallow — wizard will surface a clearer error */ }
    }
    void ensureBpmSvcJwt()
    return () => { cancelled = true }
  }, [mode])

  if (mode === 'wizard') {
    return (
      <div className="flex h-full flex-col">
        <div className="mb-4 flex items-center justify-between">
          <button
            onClick={() => setMode('list')}
            className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary"
          >
            <ArrowLeft className="h-3 w-3" /> Back to kitchen
          </button>
        </div>
        <div className="flex-1 min-h-0">
          <Onboarding onNavigate={onNavigateAdapter(setMode)} />
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col">
      <CookedFlowsList onCookNew={() => setMode('wizard')} />
    </div>
  )
}

function onNavigateAdapter(setMode: (m: Mode) => void) {
  return (_s: AdminScreen) => setMode('list')
}

function CookedFlowsList({ onCookNew }: { onCookNew: () => void }) {
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      setFlows(await listFlows())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load flows')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])

  return (
    <div className="grid h-full grid-cols-12 gap-6">
      <section className="col-span-8 flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
        <header className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-base font-semibold text-ink">Cooked flows</h2>
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              {loading ? '…' : `${flows.length} on the line`}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => void refresh()}
              disabled={loading}
              title="Refresh"
              className="flex h-7 w-7 items-center justify-center rounded border border-rule bg-card text-ink-muted transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
            >
              <RefreshCw className={cn('h-3.5 w-3.5', loading && 'animate-spin')} />
            </button>
            <button
              onClick={onCookNew}
              data-testid="cook-new-flow"
              className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary/90"
            >
              <ChefHat className="h-3.5 w-3.5" />
              Cook new flow
            </button>
          </div>
        </header>

        {error && (
          <div className="border-b border-danger/30 bg-danger/5 px-5 py-2 text-xs text-danger">
            {error}
          </div>
        )}

        {loading && flows.length === 0 && (
          <div className="flex flex-1 items-center justify-center text-sm text-ink-muted">
            Loading…
          </div>
        )}

        {!loading && flows.length === 0 && !error && (
          <div className="flex flex-1 flex-col items-center justify-center px-8 py-12 text-center">
            <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-primary/10 text-primary">
              <ChefHat className="h-7 w-7" />
            </div>
            <h3 className="text-base font-semibold text-ink">No flows cooked yet</h3>
            <p className="mt-2 max-w-sm text-sm text-ink-muted">
              Start by cooking a new flow. The 9-step kitchen walks you through
              source, structure, forms, decisions, approvers, notify, SLA, test,
              and go-live — chat on the left, canvas on the right — and produces
              a portable spec bundle.
            </p>
            <button
              onClick={onCookNew}
              className="mt-6 inline-flex items-center gap-1.5 rounded bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary/90"
            >
              <ChefHat className="h-4 w-4" />
              Cook new flow
            </button>
          </div>
        )}

        {flows.length > 0 && (
          <div className="flex-1 overflow-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 border-b border-rule bg-label-bg text-left font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
                <tr>
                  <th className="px-5 py-2 font-normal">Flow</th>
                  <th className="px-3 py-2 font-normal">Version</th>
                  <th className="px-3 py-2 font-normal">State</th>
                  <th className="px-3 py-2 font-normal">Updated</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-rule">
                {flows.map((f) => (
                  <tr key={f.id} className="hover:bg-bg">
                    <td className="px-5 py-3">
                      <div className="font-medium text-ink">{f.displayName}</div>
                      <div className="mt-0.5 font-mono text-[11px] text-ink-muted">{f.flowCode}</div>
                    </td>
                    <td className="px-3 py-3 font-mono text-xs text-ink-muted">v{f.version}</td>
                    <td className="px-3 py-3">
                      <StatePill state={f.state} />
                    </td>
                    <td className="px-3 py-3 font-mono text-[11px] text-ink-muted">
                      {formatDate(f.updatedAt)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <aside className="col-span-4 flex min-h-0 flex-col gap-4">
        <div className="rounded-lg border border-rule bg-card p-5 shadow-sm">
          <div className="mb-2 flex items-center gap-2">
            <Sparkles className="h-4 w-4 text-accent" />
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              kitchen brief
            </span>
          </div>
          <h3 className="text-sm font-semibold text-ink">How AI Kitchen works</h3>
          <ol className="mt-3 space-y-2 text-xs text-ink-muted">
            <li>
              <span className="font-semibold text-ink">1. Source — </span>
              upload an image, BPMN, or start from a preset / blank.
            </li>
            <li>
              <span className="font-semibold text-ink">2. Iterate — </span>
              chat with the assistant or edit the canvas directly. Either
              side updates the same draft.
            </li>
            <li>
              <span className="font-semibold text-ink">3. Author — </span>
              flesh out forms, decisions, approvers, notifications, SLA.
            </li>
            <li>
              <span className="font-semibold text-ink">4. Verify — </span>
              run sandbox test cases.
            </li>
            <li>
              <span className="font-semibold text-ink">5. Ship — </span>
              export a portable <code className="font-mono text-[11px]">.zip</code>
              {' '}bundle (spec.json + bpmn.xml + forms / notifications / SLA /
              actors / sample-org / test-cases).
            </li>
          </ol>
        </div>

        <div className="rounded-lg border border-dashed border-rule bg-bg/50 p-5">
          <div className="mb-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            phase status
          </div>
          <p className="text-xs leading-relaxed text-ink-muted">
            Lifecycle now persisted in admin-svc — list reads
            <code className="mx-1 font-mono text-[11px]">GET /api/flows</code>.
            Wiring the wizard to write draft spec back per step lands in the
            next sub-phase; for now the wizard still saves locally and exports
            bundles to bpm-svc's Flow Library.
          </p>
        </div>
      </aside>
    </div>
  )
}

const STATE_TONE: Record<FlowState, string> = {
  Draft:     'bg-ink-muted/15 text-ink-muted',
  Submitted: 'bg-primary/10 text-primary',
  Cooking:   'bg-accent/15 text-accent',
  OnHold:    'bg-warn/15 text-warn',
  Committed: 'bg-good/10 text-good',
  Approved:  'bg-good/15 text-good',
  Rejected:  'bg-danger/10 text-danger',
}

function StatePill({ state }: { state: FlowState }) {
  return (
    <span className={cn(
      'inline-block rounded-full px-2 py-0.5 font-mono text-[10px] tracking-[0.12em] uppercase',
      STATE_TONE[state],
    )}>
      {state.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()}
    </span>
  )
}

function formatDate(s: string): string {
  try { return new Date(s).toISOString().slice(0, 16).replace('T', ' ') }
  catch { return s }
}
