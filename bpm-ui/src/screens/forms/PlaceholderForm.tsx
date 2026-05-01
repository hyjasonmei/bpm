import { useState } from 'react'
import { FileText, Hammer } from 'lucide-react'
import { SectionCard } from '@/components/ui/card'
import { FormShell, ActionBar } from './FormShell'
import { FORMS, type FormCode } from '@/lib/workflow'
import type { PersonaCode } from '@/lib/role'

/**
 * Lightweight placeholder for forms whose detailed UI hasn't been ported
 * from the prototype yet. Still wires up the stepper + persona-aware
 * action bar so the workflow demo is end-to-end.
 */
export function PlaceholderForm({ code, persona }: { code: FormCode; persona: PersonaCode }) {
  const def = FORMS[code]
  const [activeStep, setActiveStep] = useState(def.initialActive)

  return (
    <FormShell code={code} activeStep={activeStep} setActiveStep={setActiveStep} persona={persona}>
      <SectionCard>
        <div className="flex items-start gap-4 p-6">
          <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-lg bg-amber-50 text-amber-600">
            <Hammer className="h-6 w-6" />
          </div>
          <div className="flex-1">
            <h3 className="text-base font-bold text-ink">Form layout — coming next</h3>
            <p className="mt-1 text-sm text-ink-muted">
              The detailed field layout for <span className="font-mono">{def.code}</span> is being ported
              from the prototype JSX in a follow-up commit. The workflow stepper above and the
              persona-aware action bar below are already live, so the role-switching demo flow works
              end-to-end on this form.
            </p>
            <div className="mt-3 flex items-center gap-2 text-xs text-ink-faint">
              <FileText className="h-3.5 w-3.5" />
              Prototype reference: <span className="font-mono">refs/BPM POC/js/{code === 'GEV' ? 'GEVForm.jsx' : code === 'APE' || code === 'HWP' ? 'APEHWPForms.jsx' : 'ReadOnlyViews.jsx'}</span>
            </div>
          </div>
        </div>
      </SectionCard>

      <ActionBar
        code={code}
        activeStep={activeStep}
        persona={persona}
        onSubmit={() => setActiveStep(s => s + 1)}
        onApprove={() => setActiveStep(s => s + 1)}
        onReject={() => setActiveStep(0)}
        onClose={() => {}}
      />
    </FormShell>
  )
}
