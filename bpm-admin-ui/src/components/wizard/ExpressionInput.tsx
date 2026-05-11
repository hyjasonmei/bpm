/**
 * Inline CEL-expression editor with live ✓ / ✗ validation chip.
 *
 * Used by StepForms (`conditional` / `validator` / `derivedFrom`) and
 * StepDecisions (gateway `condition`). Debounces 500 ms via
 * `useDebouncedExpressionValidator`, so each keystroke does NOT slam the
 * backend.
 *
 * The status chip is bilingual (zh-TW + en) and only appears once the user
 * has actually typed something — empty input shows a neutral placeholder.
 */
import { Check, X, Loader2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Input } from '@/components/ui/form'
import {
  useDebouncedExpressionValidator,
  type ExpressionShape,
  type ValidateSampleContext,
} from '@/lib/expressions'

export interface ExpressionInputProps {
  value: string
  onChange: (next: string) => void
  shape: ExpressionShape
  sampleContext?: ValidateSampleContext
  placeholder?: string
  className?: string
  /** Auto-test attribute hook for browser-walkthrough scripts. */
  testId?: string
}

export function ExpressionInput({
  value,
  onChange,
  shape,
  sampleContext,
  placeholder,
  className,
  testId,
}: ExpressionInputProps) {
  const state = useDebouncedExpressionValidator(value, shape, sampleContext)

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-1.5">
        <Input
          className={cn('h-7 font-mono text-[11px]', className)}
          value={value}
          onChange={e => onChange(e.target.value)}
          placeholder={placeholder}
          data-testid={testId}
        />
        <StatusChip status={state.status} />
      </div>
      {state.status === 'invalid' && state.errors.length > 0 && (
        <ul className="ml-1 list-disc pl-4 text-[10.5px] text-rose-700">
          {state.errors.map((e, i) => (
            <li key={i}>{e}</li>
          ))}
        </ul>
      )}
    </div>
  )
}

function StatusChip({ status }: { status: 'idle' | 'pending' | 'valid' | 'invalid' }) {
  if (status === 'idle') {
    return null
  }
  if (status === 'pending') {
    return (
      <span
        className="inline-flex items-center gap-1 rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-ink-muted"
        title="檢查中… / Checking…"
      >
        <Loader2 className="h-3 w-3 animate-spin" />
        <span>檢查中</span>
      </span>
    )
  }
  if (status === 'valid') {
    return (
      <span
        className="inline-flex items-center gap-1 rounded bg-emerald-50 px-1.5 py-0.5 text-[10px] font-medium text-emerald-700"
        title="運算式有效 / Valid expression"
      >
        <Check className="h-3 w-3" />
        <span>有效</span>
      </span>
    )
  }
  return (
    <span
      className="inline-flex items-center gap-1 rounded bg-rose-50 px-1.5 py-0.5 text-[10px] font-medium text-rose-700"
      title="運算式錯誤 / Invalid expression"
    >
      <X className="h-3 w-3" />
      <span>無效</span>
    </span>
  )
}
