import { ChevronRight, Check } from 'lucide-react'
import { cn } from '@/lib/cn'
import type { Step } from '@/lib/workflow'

export function Stepper({ steps, activeStep, withZh = false }: { steps: Step[]; activeStep: number; withZh?: boolean }) {
  return (
    <div className="flex items-center gap-0 overflow-x-auto py-1">
      {steps.map((s, i) => {
        const done = i < activeStep
        const current = i === activeStep
        return (
          <div key={s.id} className="flex items-center">
            <div className={cn(
              'flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold whitespace-nowrap select-none',
              current && 'rounded bg-accent text-white',
              done && 'text-slate-400',
              !done && !current && 'text-slate-400',
            )}>
              {done && <Check className="h-3 w-3 shrink-0 text-good" strokeWidth={3} />}
              <span>{s.en}</span>
              {withZh && s.zh && <span className="font-normal opacity-80">{s.zh}</span>}
            </div>
            {i < steps.length - 1 && <ChevronRight className="h-4 w-4 shrink-0 text-slate-300" strokeWidth={2} />}
          </div>
        )
      })}
    </div>
  )
}
