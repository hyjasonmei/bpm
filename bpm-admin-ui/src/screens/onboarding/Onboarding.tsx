import { useEffect, useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight, Sparkles, Download, RotateCcw, Library } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  ONBOARDING_STEPS,
  validators,
  loadDraft,
  saveDraft,
  loadStep,
  saveStep,
  resetDraft,
  EMPTY_DRAFT,
  emptySampleOrg,
  type DraftSpec,
  type OnboardingStepId,
} from '@/lib/onboarding'
import { listBundles, getBundleDraftHydration } from '@/lib/api/flowLibrary'
import type { ImportDraftResult } from '@/types/flowLibrary'
import type { AdminScreen } from '@/components/AdminLayout'
import { CoPilotCanvas } from './CoPilotCanvas'
import { StepSource } from './steps/StepSource'
import { StepStructure } from './steps/StepStructure'
import { StepForms } from './steps/StepForms'
import { StepDecisions } from './steps/StepDecisions'
import { StepApprovers } from './steps/StepApprovers'
import { StepNotify } from './steps/StepNotify'
import { StepSla } from './steps/StepSla'
import { StepTest } from './steps/StepTest'
import { StepPlaceholder } from './steps/StepPlaceholder'
import { StepGoLive } from './steps/StepGoLive'

interface OnboardingProps {
  onNavigate?: (s: AdminScreen) => void
  /**
   * Controlled mode (B2). When set, the wizard:
   *  - uses `initialDraft` as the starting draft (skipping localStorage load),
   *  - calls `onDraftChange` on every draft mutation (the host debounces +
   *    persists), and skips `saveDraft` to localStorage,
   *  - skips `saveStep` to localStorage.
   * Leave undefined for legacy AdminLayout entry which self-manages via
   * `localStorage[bpm_draft]`.
   */
  initialDraft?: DraftSpec
  onDraftChange?: (d: DraftSpec) => void
  /** Hide the legacy "Saved bundles / Export DraftSpec / Reset" strip; the
   *  host renders its own controls (Submit / Cancel / Delete). */
  hideTopBar?: boolean
}

const SESSION_DRAFT_KEY = 'bpm_draft_bundle'

/** Translate a fetched ImportDraftResult into our DraftSpec shape. */
function hydrationToDraft(h: ImportDraftResult): DraftSpec {
  // The bundle's spec.json IS a serialized DraftSpec (the wizard built it
  // that way in PR-I7 §8.3). For a hand-uploaded bundle from a different
  // source the field set may be partial — fall back to EMPTY_DRAFT and
  // overlay the SampleOrg / TestCases the parser already extracted.
  const spec = h.specJson as Partial<DraftSpec> & Record<string, unknown>
  return {
    ...EMPTY_DRAFT,
    ...spec,
    meta: { ...EMPTY_DRAFT.meta, ...(spec?.meta ?? {}) },
    flow: spec?.flow ?? EMPTY_DRAFT.flow,
    userTasks: spec?.userTasks ?? [],
    decisions: spec?.decisions ?? [],
    approvals: spec?.approvals ?? [],
    notifications: spec?.notifications ?? [],
    sla: spec?.sla ?? EMPTY_DRAFT.sla,
    integrations: spec?.integrations ?? EMPTY_DRAFT.integrations,
    sampleOrg: h.sampleOrg && h.sampleOrg.users ? h.sampleOrg : emptySampleOrg(),
    testCases: h.testCases ?? [],
  }
}

/** Walk validators in step order; return the first failing index, or
 *  the go_live index if all pass. */
function pickStepFromValidation(d: DraftSpec): number {
  for (let i = 0; i < ONBOARDING_STEPS.length; i++) {
    const id = ONBOARDING_STEPS[i].id as OnboardingStepId
    if (!validators[id](d).valid) return i
  }
  return ONBOARDING_STEPS.findIndex(s => s.id === 'go_live')
}

export function Onboarding({
  onNavigate,
  initialDraft,
  onDraftChange,
  hideTopBar,
}: OnboardingProps = {}) {
  const controlled = onDraftChange !== undefined
  const [draft, setDraft] = useState<DraftSpec>(() => initialDraft ?? loadDraft())
  const [stepIdx, setStepIdx] = useState<number>(() => (controlled ? 0 : loadStep()))
  const [savedBundleCount, setSavedBundleCount] = useState<number | null>(null)
  const [hydrating, setHydrating] = useState(false)
  const step = ONBOARDING_STEPS[stepIdx]

  useEffect(() => {
    if (controlled) onDraftChange?.(draft)
    else saveDraft(draft)
  }, [draft, controlled, onDraftChange])
  useEffect(() => { if (!controlled) saveStep(stepIdx) }, [stepIdx, controlled])

  // Saved-bundles indicator (PR-I6 task §10.2). Best-effort fetch — if the
  // backend is unreachable or auth hasn't kicked in yet, we just hide the
  // pill instead of surfacing an error from a non-essential affordance.
  useEffect(() => {
    let cancelled = false
    listBundles()
      .then(items => { if (!cancelled) setSavedBundleCount(items.length) })
      .catch(() => { /* silently hide */ })
    return () => { cancelled = true }
  }, [])

  /**
   * PR-I7 §8.1 — Hydrate from `?bundle=<id>` query param on mount.
   *
   * Two paths:
   *  - `?bundle=<guid>` — saved bundle row in Flow Library; fetch its
   *    hydration payload from the new GET /{id}/hydration endpoint.
   *  - `?bundle=draft` — fresh import the user kicked off via
   *    ImportModal; the parsed payload was stashed in sessionStorage
   *    so we don't have to re-upload the file just to read it back.
   *
   * Either way we strip the query param via history.replaceState so a
   * page refresh doesn't re-trigger the hydration.
   */
  useEffect(() => {
    let cancelled = false
    const url = new URL(window.location.href)
    const bundleParam = url.searchParams.get('bundle')
    if (!bundleParam) return

    setHydrating(true)
    const finish = (result: ImportDraftResult | null, err?: string) => {
      if (cancelled) return
      try {
        url.searchParams.delete('bundle')
        window.history.replaceState(null, '', url.toString())
      } catch { /* ignore */ }
      try { sessionStorage.removeItem(SESSION_DRAFT_KEY) } catch { /* ignore */ }
      if (result) {
        const next = hydrationToDraft(result)
        setDraft(next)
        setStepIdx(pickStepFromValidation(next))
      } else if (err) {
        // Surface in console only — a popup would be too aggressive for
        // an auto-hydration that the user merely landed on. The wizard
        // continues with whatever localStorage draft they had.
        console.warn('[Onboarding] bundle hydration failed:', err)
      }
      setHydrating(false)
    }

    if (bundleParam === 'draft') {
      // Fresh import handed off via sessionStorage by ImportModal.
      try {
        const raw = sessionStorage.getItem(SESSION_DRAFT_KEY)
        if (!raw) {
          finish(null, 'sessionStorage marker missing — re-import the bundle')
          return
        }
        const parsed = JSON.parse(raw) as ImportDraftResult
        finish(parsed)
      } catch (e) {
        finish(null, e instanceof Error ? e.message : String(e))
      }
      return
    }

    // GUID path — saved bundle row.
    void getBundleDraftHydration(bundleParam)
      .then(r => finish(r))
      .catch(e => finish(null, e instanceof Error ? e.message : String(e)))

    return () => { cancelled = true }
  }, [])

  const validation = useMemo(() => validators[step.id](draft), [step.id, draft])

  const goNext = () => {
    if (stepIdx < ONBOARDING_STEPS.length - 1) setStepIdx(stepIdx + 1)
  }
  const goBack = () => {
    if (stepIdx > 0) setStepIdx(stepIdx - 1)
  }
  const reset = () => {
    if (!confirm('清除所有 onboarding 草稿？')) return
    resetDraft()
    setDraft(loadDraft())
    setStepIdx(0)
  }

  /**
   * Dev-only escape hatch (PR-I7 §8.3 keeps the JSON download for
   * debugging, but renames it so the canonical flow is "Save bundle").
   */
  const exportSpec = () => {
    const blob = new Blob([JSON.stringify(draft, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${draft.meta.tenant || 'tenant'}_${draft.meta.flowCode || 'flow'}_v${draft.meta.flowVersion}.json`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  }

  return (
    <div className="flex flex-col gap-3">
      {!hideTopBar && (
        /* Top bar: title + reset (legacy entry only — controlled host hides this) */
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-bold flex items-center gap-2">
              <Sparkles className="h-5 w-5 text-accent" />
              AI Onboarding
            </h1>
            <p className="text-xs text-ink-muted">9 個 step 把流程規格談清楚，最後產生可攜帶的 spec bundle，存到 Flow Library。</p>
          </div>
          <div className="flex items-center gap-2">
            {hydrating && (
              <span className="text-[11px] text-ink-faint">Hydrating from saved bundle…</span>
            )}
            {savedBundleCount !== null && (
              <button
                onClick={() => onNavigate?.({ kind: 'flow-library' })}
                className="flex items-center gap-1.5 rounded border border-rule bg-white px-3 py-1.5 text-xs font-medium text-ink-muted hover:bg-slate-50 hover:text-ink"
                title="Open Flow Library"
              >
                <Library className="h-3.5 w-3.5" />
                Saved bundles: <span className="font-semibold text-ink">{savedBundleCount}</span>
              </button>
            )}
            <button onClick={exportSpec} className="flex items-center gap-1.5 rounded border border-rule bg-white px-3 py-1.5 text-xs font-medium text-ink hover:bg-slate-50" title="Debug only — canonical export is 'Save to Flow Library'">
              <Download className="h-3.5 w-3.5" /> Export DraftSpec (debug)
            </button>
            <button onClick={reset} className="flex items-center gap-1.5 rounded border border-rule bg-white px-3 py-1.5 text-xs font-medium text-ink-muted hover:bg-slate-50 hover:text-danger">
              <RotateCcw className="h-3.5 w-3.5" /> Reset
            </button>
          </div>
        </div>
      )}

      {/* Stepper bar */}
      <div className="rounded-md border border-rule bg-card px-3 py-2">
        <div className="flex items-center gap-0 overflow-x-auto">
          {ONBOARDING_STEPS.map((s, i) => {
            const done = i < stepIdx
            const current = i === stepIdx
            return (
              <button
                key={s.id}
                onClick={() => setStepIdx(i)}
                className="flex items-center gap-1 whitespace-nowrap"
              >
                <div className={cn(
                  'flex items-center gap-2 rounded px-2.5 py-1 text-[11px] font-semibold uppercase tracking-wider transition-colors',
                  current && 'bg-accent text-white',
                  done && 'text-good',
                  !done && !current && 'text-ink-faint hover:text-ink',
                )}>
                  <span className="font-mono">{i + 1}</span>
                  <span>{s.en}</span>
                  <span className="font-normal opacity-80 normal-case">{s.zh}</span>
                </div>
                {i < ONBOARDING_STEPS.length - 1 && <ChevronRight className="h-3.5 w-3.5 shrink-0 text-slate-300" />}
              </button>
            )
          })}
        </div>
      </div>

      {/* Body — co-pilot canvas */}
      <CoPilotCanvas
        step={step}
        draft={draft}
        setDraft={setDraft}
        canvas={renderCanvas(step.id, draft, setDraft, onNavigate)}
      />

      {/* Footer — back / next */}
      <div className="flex items-center justify-between rounded-md border border-rule bg-card px-3 py-2">
        <button
          onClick={goBack}
          disabled={stepIdx === 0}
          className="flex items-center gap-1 rounded px-3 py-1.5 text-sm font-medium text-ink-muted hover:bg-slate-100 disabled:opacity-30 disabled:hover:bg-transparent"
        >
          <ChevronLeft className="h-4 w-4" /> Back
        </button>

        <ValidationDisplay errors={validation.errors} valid={validation.valid} />

        <button
          onClick={goNext}
          disabled={!validation.valid || stepIdx === ONBOARDING_STEPS.length - 1}
          className={cn(
            'flex items-center gap-1 rounded px-4 py-1.5 text-sm font-semibold transition-colors',
            validation.valid && stepIdx < ONBOARDING_STEPS.length - 1
              ? 'bg-primary text-white hover:bg-blue-700'
              : 'bg-slate-200 text-slate-400 cursor-not-allowed',
          )}
        >
          Next <ChevronRight className="h-4 w-4" />
        </button>
      </div>
    </div>
  )
}

function renderCanvas(stepId: string, draft: DraftSpec, setDraft: (d: DraftSpec) => void, onNavigate?: (s: AdminScreen) => void) {
  switch (stepId) {
    case 'source':    return <StepSource draft={draft} setDraft={setDraft} />
    case 'structure': return <StepStructure draft={draft} setDraft={setDraft} />
    case 'forms':     return <StepForms draft={draft} setDraft={setDraft} />
    case 'decisions': return <StepDecisions draft={draft} setDraft={setDraft} />
    case 'approvers': return <StepApprovers draft={draft} setDraft={setDraft} />
    case 'notify':    return <StepNotify draft={draft} setDraft={setDraft} />
    case 'sla':       return <StepSla draft={draft} setDraft={setDraft} />
    case 'test':      return <StepTest draft={draft} setDraft={setDraft} />
    case 'go_live':   return <StepGoLive draft={draft} onNavigate={onNavigate} />
    default:          return <StepPlaceholder stepId={stepId} draft={draft} />
  }
}

function ValidationDisplay({ errors, valid }: { errors: string[]; valid: boolean }) {
  if (valid) return <span className="text-xs text-good font-medium">✓ Validator pass — 可以下一步</span>
  return (
    <div className="flex flex-col items-end text-[11px] text-danger max-w-md">
      <span className="font-semibold">✗ Validator 阻擋下一步：</span>
      <ul className="text-right">
        {errors.slice(0, 3).map((e, i) => <li key={i}>· {e}</li>)}
        {errors.length > 3 && <li>...還有 {errors.length - 3} 條</li>}
      </ul>
    </div>
  )
}
