import { useCallback, useEffect, useRef, useState } from 'react'
import {
  ArrowLeft,
  ChefHat,
  CheckCircle2,
  Download,
  RefreshCw,
  Save,
  Sparkles,
  Trash2,
  Undo2,
  X,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import { EMPTY_DRAFT, type DraftSpec } from '@/lib/onboarding'
import { flowToBpmnXml } from '@/lib/bpmnXml'
import {
  cancelFlow,
  createFlow,
  deleteFlow,
  type FlowDetail,
  type FlowState,
  type FlowSummary,
  getFlow,
  listFlows,
  submitFlow,
  updateFlowSpec,
} from '@/flowcook/api/flows'

type Mode = 'list' | 'wizard'

export function AiKitchenPage() {
  const [mode, setMode] = useState<Mode>('list')
  const [activeFlow, setActiveFlow] = useState<FlowDetail | null>(null)

  // Phase D moved /api/chat + /api/spec-extract onto admin-svc, so the
  // wizard no longer needs a bpm-svc JWT — the existing fc_session cookie
  // covers both. Previous ensureBpmSvcJwt() effect deleted.

  const openFlow = useCallback(async (id: string) => {
    const flow = await getFlow(id)
    setActiveFlow(flow)
    setMode('wizard')
  }, [])

  const backToList = useCallback(() => {
    setActiveFlow(null)
    setMode('list')
  }, [])

  if (mode === 'wizard' && activeFlow) {
    return (
      <WizardView
        flow={activeFlow}
        onFlowChange={setActiveFlow}
        onClose={backToList}
      />
    )
  }

  return (
    <CookedFlowsList onOpenFlow={openFlow} />
  )
}

// ──────────────────────────────────────────────────────────────
// List
// ──────────────────────────────────────────────────────────────

function CookedFlowsList({ onOpenFlow }: { onOpenFlow: (id: string) => Promise<void> }) {
  const [flows, setFlows] = useState<FlowSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)

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

  async function cookNew(flowCode: string, displayName: string) {
    const flow = await createFlow({
      flowCode,
      displayName,
      specJson: JSON.stringify(EMPTY_DRAFT),
    })
    setCreating(false)
    await onOpenFlow(flow.id)
  }

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
              onClick={() => setCreating(true)}
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
              onClick={() => setCreating(true)}
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
                  <tr
                    key={f.id}
                    onClick={() => void onOpenFlow(f.id)}
                    data-testid={`flow-row-${f.flowCode}`}
                    className="cursor-pointer hover:bg-bg"
                  >
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
            <li><span className="font-semibold text-ink">1. Source — </span>upload an image, BPMN, or start from a preset / blank.</li>
            <li><span className="font-semibold text-ink">2. Iterate — </span>chat with the assistant or edit the canvas directly. Either side updates the same draft.</li>
            <li><span className="font-semibold text-ink">3. Author — </span>flesh out forms, decisions, approvers, notifications, SLA.</li>
            <li><span className="font-semibold text-ink">4. Verify — </span>run sandbox test cases.</li>
            <li><span className="font-semibold text-ink">5. Ship — </span>Submit hands the draft to chef. Export a portable <code className="font-mono text-[11px]">.zip</code> bundle on Step 9 if you want a snapshot.</li>
          </ol>
        </div>

        <div className="rounded-lg border border-dashed border-rule bg-bg/50 p-5">
          <div className="mb-2 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            phase status
          </div>
          <p className="text-xs leading-relaxed text-ink-muted">
            Drafts now auto-save to admin-svc as you edit. Click a row to
            resume an existing draft. Submit moves it into chef's queue
            (state = submitted); chef-side handlers land in step 7.
          </p>
        </div>
      </aside>

      {creating && (
        <CookNewFlowModal onCreate={cookNew} onCancel={() => setCreating(false)} />
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Cook new flow modal
// ──────────────────────────────────────────────────────────────

function CookNewFlowModal({
  onCreate,
  onCancel,
}: {
  onCreate: (flowCode: string, displayName: string) => Promise<void>
  onCancel: () => void
}) {
  const [flowCode, setFlowCode] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!flowCode.trim() || !displayName.trim()) {
      setError('Both fields are required.')
      return
    }
    setSaving(true)
    setError(null)
    try {
      await onCreate(flowCode.trim().toUpperCase(), displayName.trim())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed')
      setSaving(false)
    }
  }

  return (
    <div className="fixed inset-0 z-30 flex items-center justify-center bg-ink/30 p-4">
      <form
        onSubmit={submit}
        className="w-full max-w-md rounded-lg border border-rule bg-card p-6 shadow-lg"
      >
        <div className="mb-2 flex items-center gap-2">
          <ChefHat className="h-4 w-4 text-primary" />
          <h3 className="text-base font-semibold text-ink">Cook a new flow</h3>
        </div>
        <p className="mb-5 text-xs text-ink-muted">
          Name the flow first. You'll be able to edit everything inside the wizard.
        </p>

        <label className="mb-3 block">
          <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            Flow code
          </span>
          <input
            value={flowCode}
            onChange={(e) => setFlowCode(e.target.value)}
            placeholder="e.g. LEAVE"
            autoFocus
            className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm uppercase text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
          />
        </label>
        <label className="mb-4 block">
          <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            Display name
          </span>
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="e.g. Leave Request"
            className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
          />
        </label>

        {error && (
          <p className="mb-3 rounded border border-danger/30 bg-danger/5 px-3 py-2 text-xs text-danger">
            {error}
          </p>
        )}

        <div className="flex items-center justify-end gap-2">
          <button
            type="button"
            onClick={onCancel}
            disabled={saving}
            className="rounded px-3 py-1.5 text-xs text-ink-muted hover:text-ink"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1.5 text-xs font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
          >
            <ChefHat className="h-3.5 w-3.5" />
            {saving ? 'Creating…' : 'Open the kitchen'}
          </button>
        </div>
      </form>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Wizard view — wraps legacy Onboarding in controlled mode
// ──────────────────────────────────────────────────────────────

const AUTOSAVE_DEBOUNCE_MS = 600

function WizardView({
  flow,
  onFlowChange,
  onClose,
}: {
  flow: FlowDetail
  onFlowChange: (f: FlowDetail) => void
  onClose: () => void
}) {
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [saveError, setSaveError] = useState<string | null>(null)
  const [transitionPending, setTransitionPending] = useState<null | 'submit' | 'cancel' | 'delete'>(null)
  const [downloading, setDownloading] = useState(false)
  const timerRef = useRef<number | null>(null)
  const latestDraftRef = useRef<DraftSpec | null>(null)

  // Parse initial spec into a DraftSpec; tolerate parse errors by falling
  // back to EMPTY_DRAFT (a partial spec from chef-on-hold or an external
  // import is the only realistic way to land here with bad JSON).
  const initialDraft: DraftSpec = (() => {
    try {
      if (!flow.specJson || flow.specJson === '{}') return EMPTY_DRAFT
      return { ...EMPTY_DRAFT, ...JSON.parse(flow.specJson) }
    } catch {
      return EMPTY_DRAFT
    }
  })()

  const persistDraft = useCallback(async (draft: DraftSpec) => {
    setSaveState('saving')
    setSaveError(null)
    try {
      const updated = await updateFlowSpec(flow.id, {
        specJson: JSON.stringify(draft),
      })
      onFlowChange(updated)
      setSaveState('saved')
    } catch (err) {
      setSaveState('error')
      setSaveError(err instanceof Error ? err.message : 'Save failed')
    }
  }, [flow.id, onFlowChange])

  const handleDraftChange = useCallback((draft: DraftSpec) => {
    latestDraftRef.current = draft
    if (flow.state !== 'Draft') return // read-only once submitted
    if (timerRef.current) window.clearTimeout(timerRef.current)
    timerRef.current = window.setTimeout(() => {
      void persistDraft(draft)
    }, AUTOSAVE_DEBOUNCE_MS)
  }, [flow.state, persistDraft])

  useEffect(() => () => {
    if (timerRef.current) window.clearTimeout(timerRef.current)
  }, [])

  async function submit() {
    setTransitionPending('submit')
    try {
      // Flush any pending autosave first.
      if (timerRef.current) {
        window.clearTimeout(timerRef.current)
        if (latestDraftRef.current) await persistDraft(latestDraftRef.current)
      }
      const updated = await submitFlow(flow.id)
      onFlowChange(updated)
      onClose()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Submit failed')
    } finally {
      setTransitionPending(null)
    }
  }

  async function cancel() {
    if (!window.confirm('Cancel this flow and return it to Draft?')) return
    setTransitionPending('cancel')
    try {
      const updated = await cancelFlow(flow.id)
      onFlowChange(updated)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Cancel failed')
    } finally {
      setTransitionPending(null)
    }
  }

  async function downloadBundle() {
    setDownloading(true)
    try {
      // Flush pending autosave so the server-side spec matches what the
      // user just saw on screen.
      if (timerRef.current) {
        window.clearTimeout(timerRef.current)
        if (latestDraftRef.current) await persistDraft(latestDraftRef.current)
      }
      const draft = latestDraftRef.current ?? initialDraft
      const bpmnXml = await flowToBpmnXml(draft)
      // The bundle validator requires >=1 user + >=1 test-case so the
      // produced zip is "runnable". If the wizard hasn't gathered them
      // yet (early-stage Cook new flow), stub a placeholder so download
      // remains demoable. The TODO marker keeps the unfinished signal
      // present in the spec.md the builder emits.
      const testCases = draft.testCases.length > 0 ? draft.testCases : [{
        id: 'placeholder-happy',
        name: 'Happy path (placeholder — fill in step 8)',
        inputs: {},
        expectedTrace: [],
        expectedFinalStatus: 'Completed',
      }]
      const res = await fetch(`/api/flows/${flow.id}/bundle`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          bpmnXml,
          sampleOrg: draft.sampleOrg,
          testCases,
          sourceInstanceId: `flow:${flow.id}`,
        }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`)
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${flow.flowCode}_v${flow.version}.zip`
      document.body.appendChild(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Bundle download failed')
    } finally {
      setDownloading(false)
    }
  }

  async function softDelete() {
    if (!window.confirm(`Delete draft "${flow.displayName}"? This soft-deletes the row.`)) return
    setTransitionPending('delete')
    try {
      await deleteFlow(flow.id)
      onClose()
    } catch (err) {
      window.alert(err instanceof Error ? err.message : 'Delete failed')
      setTransitionPending(null)
    }
  }

  const canSubmit = flow.state === 'Draft'
  const canCancel = flow.state === 'Submitted' || flow.state === 'Cooking' || flow.state === 'OnHold'
  const canDelete = flow.state === 'Draft'

  return (
    <div className="flex h-full flex-col">
      <div className="mb-4 flex items-center justify-between gap-3 flex-wrap">
        <div className="flex items-center gap-3">
          <button
            onClick={onClose}
            className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary"
          >
            <ArrowLeft className="h-3 w-3" /> Back to kitchen
          </button>
          <div className="flex items-baseline gap-2">
            <h2 className="text-sm font-semibold text-ink">{flow.displayName}</h2>
            <span className="font-mono text-[11px] text-ink-muted">{flow.flowCode} · v{flow.version}</span>
            <StatePill state={flow.state} />
          </div>
        </div>

        <div className="flex items-center gap-2">
          <SaveStatusBadge state={saveState} error={saveError} />
          <button
            onClick={() => void downloadBundle()}
            disabled={downloading || transitionPending !== null}
            title="Build a portable .zip bundle from the current draft"
            className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
          >
            <Download className="h-3 w-3" />
            {downloading ? 'Bundling…' : 'Download bundle'}
          </button>
          {canCancel && (
            <button
              onClick={() => void cancel()}
              disabled={transitionPending !== null}
              className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted transition-colors hover:border-warn hover:text-warn disabled:opacity-50"
            >
              <Undo2 className="h-3 w-3" /> Cancel
            </button>
          )}
          {canDelete && (
            <button
              onClick={() => void softDelete()}
              disabled={transitionPending !== null}
              className="inline-flex items-center gap-1 rounded border border-danger/30 bg-card px-2.5 py-1 text-xs font-medium text-danger transition-colors hover:bg-danger/10 disabled:opacity-50"
            >
              <Trash2 className="h-3 w-3" /> Delete draft
            </button>
          )}
          {canSubmit && (
            <button
              onClick={() => void submit()}
              disabled={transitionPending !== null}
              className="inline-flex items-center gap-1.5 rounded bg-primary px-3 py-1 text-xs font-semibold text-white transition-colors hover:bg-primary/90 disabled:opacity-50"
            >
              <CheckCircle2 className="h-3.5 w-3.5" />
              {transitionPending === 'submit' ? 'Submitting…' : 'Submit to chef'}
            </button>
          )}
        </div>
      </div>

      <div className="flex-1 min-h-0">
        <Onboarding
          initialDraft={initialDraft}
          onDraftChange={handleDraftChange}
          hideTopBar
        />
      </div>
    </div>
  )
}

function SaveStatusBadge({ state, error }: { state: 'idle' | 'saving' | 'saved' | 'error'; error: string | null }) {
  if (state === 'saving') {
    return (
      <span className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        <Save className="h-3 w-3 animate-pulse" /> Saving…
      </span>
    )
  }
  if (state === 'saved') {
    return (
      <span className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.14em] uppercase text-good">
        <CheckCircle2 className="h-3 w-3" /> Saved
      </span>
    )
  }
  if (state === 'error') {
    return (
      <span title={error ?? undefined} className="inline-flex items-center gap-1 font-mono text-[10px] tracking-[0.14em] uppercase text-danger">
        <X className="h-3 w-3" /> Save failed
      </span>
    )
  }
  return null
}

// ──────────────────────────────────────────────────────────────
// Shared bits
// ──────────────────────────────────────────────────────────────

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
