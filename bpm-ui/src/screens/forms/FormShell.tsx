import { useState } from 'react'
import { Workflow as WorkflowIcon, ExternalLink, Printer } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { FORMS, type FormCode, ownerLabel } from '@/lib/workflow'
import { PERSONAS, type PersonaCode } from '@/lib/role'

interface FormShellProps {
  code: FormCode
  activeStep: number
  setActiveStep: (n: number) => void
  persona: PersonaCode
  /** info row content — overrides default Requestor / Cost Center / Business Unit */
  infoRow?: React.ReactNode
  copySelector?: boolean
  rightActions?: React.ReactNode
  children: React.ReactNode
}

export function FormShell({
  code, activeStep, setActiveStep, persona,
  infoRow, copySelector = true, rightActions, children,
}: FormShellProps) {
  const def = FORMS[code]
  const [bpmnOpen, setBpmnOpen] = useState(false)

  return (
    <div className="space-y-4">
      {/* Stepper bar — sits below the global header's form sub-header */}
      <SectionCard className="!p-0">
        <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2">
          <Stepper steps={def.steps} activeStep={activeStep} withZh />
          <Button variant="outline" size="xs" onClick={() => setBpmnOpen(true)}>
            <WorkflowIcon className="h-3 w-3" /> View BPMN
          </Button>
        </div>

        {/* simulated 'jump-to-step' control for the demo so the user can see different roles' surfaces */}
        <div className="flex items-center gap-2 border-b border-rule bg-amber-50/40 px-4 py-1.5 text-[11px] text-amber-800">
          <span className="font-semibold uppercase tracking-wider">Demo</span>
          <span>jump to step:</span>
          {def.steps.map((s, i) => (
            <button
              key={s.id}
              onClick={() => setActiveStep(i)}
              className={cn(
                'rounded px-1.5 py-0.5 text-[10.5px] font-medium uppercase transition-colors',
                i === activeStep ? 'bg-accent text-white' : 'hover:bg-amber-100 text-amber-700',
              )}
              title={`Owned by ${ownerLabel(def.ownerByStep[i])}`}
            >
              {s.en}
            </button>
          ))}
        </div>

        <div className="px-5 py-4">
          {/* Title + copy selector / read-only actions */}
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h1 className="text-xl font-bold text-ink">{def.label}</h1>
              <p className="text-[11px] uppercase tracking-wider text-ink-muted">{def.zhLabel}</p>
            </div>
            <div className="flex items-center gap-2">
              {rightActions}
              {copySelector && (
                <select className="h-8 rounded-md border border-rule bg-white px-3 text-sm text-ink-muted">
                  <option>Copy from my existing requests</option>
                </select>
              )}
              <Button variant="ghost" size="sm" title="Share permanent link"><ExternalLink className="h-3.5 w-3.5" /></Button>
              <Button variant="ghost" size="sm" title="Print"><Printer className="h-3.5 w-3.5" /></Button>
            </div>
          </div>

          {/* Info row */}
          {infoRow ?? <DefaultInfoRow persona={persona} />}
        </div>
      </SectionCard>

      {/* Form body */}
      {children}

      <BpmnView
        open={bpmnOpen}
        steps={def.steps}
        activeStep={activeStep}
        ownerByStep={def.ownerByStep}
        formLabel={`${def.code} — ${def.label}`}
        onClose={() => setBpmnOpen(false)}
      />
    </div>
  )
}

function DefaultInfoRow({ persona }: { persona: PersonaCode }) {
  const u = PERSONAS[persona].user
  return (
    <SectionCard className="mt-4">
      <div className="grid grid-cols-2 divide-x divide-rule">
        <div className="grid grid-cols-[110px_1fr] gap-x-4 gap-y-1 p-3 text-sm">
          <span className="text-ink-muted">Requestor</span>
          <span className="font-medium text-ink">{u.name}</span>
          <span className="text-ink-muted">Cost Center</span>
          <span className="text-ink">{u.dept}</span>
        </div>
        <div className="grid grid-cols-[110px_1fr] gap-x-4 gap-y-1 p-3 text-sm">
          <span className="text-ink-muted">Business Unit</span>
          <span className="text-ink">Taiwan (Taipei)</span>
          <span className="text-ink-muted">Persona</span>
          <span className="text-ink">{PERSONAS[persona].displayName} · {PERSONAS[persona].zhName}</span>
        </div>
      </div>
    </SectionCard>
  )
}

/** Bottom action bar that adapts to the active persona's relationship to the active step. */
export function ActionBar({
  code, activeStep, persona,
  onSubmit, onApprove, onReject, onClose,
}: {
  code: FormCode
  activeStep: number
  persona: PersonaCode
  onSubmit?: () => void
  onApprove?: () => void
  onReject?: () => void
  onClose?: () => void
}) {
  const def = FORMS[code]
  const owner = def.ownerByStep[activeStep]
  const isAdmin = persona === 'admin'
  const isOwner = owner === persona || isAdmin
  const isTerminal = owner === null

  if (isTerminal) {
    return (
      <SectionCard>
        <SectionTitle>Action</SectionTitle>
        <div className="flex items-center justify-between gap-3 px-4 py-3">
          <p className="text-sm text-ink-muted">
            ✓ This case is closed.{' '}
            {isAdmin && <span className="text-xs text-ink-faint">(Admin can re-open via system console.)</span>}
          </p>
          <Button variant="outline" size="sm" onClick={onClose}>Copy to New</Button>
        </div>
      </SectionCard>
    )
  }

  if (!isOwner) {
    return (
      <SectionCard>
        <div className="flex items-center justify-between gap-3 border-l-4 border-amber-300 bg-amber-50 px-4 py-3">
          <div>
            <p className="text-sm font-semibold text-amber-900">View only</p>
            <p className="text-xs text-amber-800">
              Awaiting {ownerLabel(owner)} action. Switch persona to act on this step.
            </p>
          </div>
        </div>
      </SectionCard>
    )
  }

  // Owner of the current step — render contextual actions
  if (activeStep === 0) {
    // First step = applicant action
    return (
      <SectionCard>
        <div className="flex items-center justify-end gap-2 px-4 py-3">
          <Button variant="outline" size="md">Save as Draft</Button>
          <Button variant="primary" size="md" onClick={onSubmit}>Submit</Button>
        </div>
      </SectionCard>
    )
  }

  // Approver / reviewer surface
  return (
    <SectionCard>
      <div className="flex items-center justify-end gap-2 px-4 py-3">
        <Button variant="destructive" size="md" onClick={onReject}>Reject</Button>
        <Button variant="primary" size="md" onClick={onApprove}>
          {owner === 'finance' ? 'Confirm & Forward' : owner === 'hr' ? 'Record & Close' : owner === 'it' ? 'Submit Spec' : 'Approve'}
        </Button>
      </div>
    </SectionCard>
  )
}
