import { useEffect, useState } from 'react'
import { ArrowLeft, ChefHat, Sparkles } from 'lucide-react'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import type { AdminScreen } from '@/components/AdminLayout'
import { apiFetch, getJwt, setJwt } from '@/lib/apiFetch'

type Mode = 'list' | 'wizard'

/**
 * AI Kitchen — entry page for the flow-design experience.
 *
 * Phase A (this iteration):
 *   - List view shows an empty state + "Cook new flow" CTA.
 *   - Wizard view directly mounts the legacy `<Onboarding />` 9-step
 *     experience (chat + canvas + bundle export against bpm-svc).
 *
 * Phase B (later, per flowcook-step3):
 *   - Wire to an admin-svc lifecycle API (FlowDraft / FlowVersion) so the
 *     list shows real "cooked" flows by state.
 *   - Author the four new step3 stages (TRIGGER&ACCESS / VARIABLES /
 *     INTEGRATIONS / TRANSLATION) and switch the bundle producer to
 *     admin-svc.
 */
export function AiKitchenPage() {
  const [mode, setMode] = useState<Mode>('list')

  // Wizard talks to bpm-svc directly via `apiFetch`, which uses a JWT
  // bearer from localStorage. The legacy AdminLayout used to auto-mint
  // this on entry; under flowcook we do the same here so the wizard can
  // hit /api/chat / /api/admin/flow-library / /api/spec-extract without
  // a 401.
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
      } catch { /* the wizard will surface a clearer error if calls fail */ }
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
  // Wizard's legacy `onNavigate` calls were aimed at sibling Admin
  // screens (`flow-library`, etc.). Under flowcook the closest match is
  // "back to the kitchen list" — accept anything and drop back there.
  return (_s: AdminScreen) => setMode('list')
}

function CookedFlowsList({ onCookNew }: { onCookNew: () => void }) {
  return (
    <div className="grid h-full grid-cols-12 gap-6">
      {/* Left — the kitchen pass; empty until lifecycle BE lands */}
      <section className="col-span-8 flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
        <header className="flex items-center justify-between border-b border-rule px-5 py-3">
          <div className="flex items-baseline gap-3">
            <h2 className="text-base font-semibold text-ink">Cooked flows</h2>
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">0 on the line</span>
          </div>
          <button
            onClick={onCookNew}
            data-testid="cook-new-flow"
            className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary/90"
          >
            <ChefHat className="h-3.5 w-3.5" />
            Cook new flow
          </button>
        </header>

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
      </section>

      {/* Right — kitchen brief / context strip */}
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
            Lifecycle persistence (drafts, versions, on-hold callbacks from
            chef) lands in a later step — for now bundles are produced
            client-side per session and saved to the Flow Library on
            bpm-svc.
          </p>
        </div>
      </aside>
    </div>
  )
}
