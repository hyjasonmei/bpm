import { useEffect, useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight, Sparkles, Download, RotateCcw } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  ONBOARDING_STEPS,
  validators,
  loadDraft,
  saveDraft,
  loadStep,
  saveStep,
  resetDraft,
  type DraftSpec,
} from '@/lib/onboarding'
import { CoPilotCanvas } from './CoPilotCanvas'
import { StepSource } from './steps/StepSource'
import { StepStructure } from './steps/StepStructure'
import { StepForms } from './steps/StepForms'
import { StepDecisions } from './steps/StepDecisions'
import { StepApprovers } from './steps/StepApprovers'
import { StepTest } from './steps/StepTest'
import { StepPlaceholder } from './steps/StepPlaceholder'
import { StepGoLive } from './steps/StepGoLive'

export function Onboarding() {
  const [draft, setDraft] = useState<DraftSpec>(() => loadDraft())
  const [stepIdx, setStepIdx] = useState<number>(() => loadStep())
  const step = ONBOARDING_STEPS[stepIdx]

  useEffect(() => { saveDraft(draft) }, [draft])
  useEffect(() => { saveStep(stepIdx) }, [stepIdx])

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
      {/* Top bar: title + reset */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-bold flex items-center gap-2">
            <Sparkles className="h-5 w-5 text-accent" />
            AI Onboarding
          </h1>
          <p className="text-xs text-ink-muted">9 個 step 跟 AI 把流程規格談清楚 — 完成後 spec 自動送至後台 Claude Code 部署管線</p>
        </div>
        <div className="flex items-center gap-2">
          <button onClick={exportSpec} className="flex items-center gap-1.5 rounded border border-rule bg-white px-3 py-1.5 text-xs font-medium text-ink hover:bg-slate-50">
            <Download className="h-3.5 w-3.5" /> Export Draft Spec
          </button>
          <button onClick={reset} className="flex items-center gap-1.5 rounded border border-rule bg-white px-3 py-1.5 text-xs font-medium text-ink-muted hover:bg-slate-50 hover:text-danger">
            <RotateCcw className="h-3.5 w-3.5" /> Reset
          </button>
        </div>
      </div>

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
        canvas={renderCanvas(step.id, draft, setDraft)}
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

function renderCanvas(stepId: string, draft: DraftSpec, setDraft: (d: DraftSpec) => void) {
  switch (stepId) {
    case 'source':    return <StepSource draft={draft} setDraft={setDraft} />
    case 'structure': return <StepStructure draft={draft} setDraft={setDraft} />
    case 'forms':     return <StepForms draft={draft} setDraft={setDraft} />
    case 'decisions': return <StepDecisions draft={draft} setDraft={setDraft} />
    case 'approvers': return <StepApprovers draft={draft} setDraft={setDraft} />
    case 'test':      return <StepTest draft={draft} setDraft={setDraft} />
    case 'go_live':   return <StepGoLive draft={draft} />
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
