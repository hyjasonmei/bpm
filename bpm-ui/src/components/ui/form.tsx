import * as React from 'react'
import { ChevronDown, Info } from 'lucide-react'
import { cn } from '@/lib/cn'

/* Inputs */
export const Input = React.forwardRef<HTMLInputElement, React.InputHTMLAttributes<HTMLInputElement>>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        'flex h-8 w-full rounded-md border border-rule bg-card px-3 text-sm text-ink',
        'placeholder:text-ink-faint',
        'focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary',
        'disabled:bg-slate-50 disabled:text-ink-muted',
        'read-only:bg-slate-50 read-only:text-ink-muted read-only:focus:ring-0 read-only:focus:border-rule',
        className,
      )}
      {...props}
    />
  ),
)
Input.displayName = 'Input'

export const Textarea = React.forwardRef<HTMLTextAreaElement, React.TextareaHTMLAttributes<HTMLTextAreaElement>>(
  ({ className, ...props }, ref) => (
    <textarea
      ref={ref}
      className={cn(
        'flex w-full rounded-md border border-rule bg-card px-3 py-2 text-sm text-ink',
        'placeholder:text-ink-faint',
        'focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary',
        'resize-none',
        'read-only:bg-slate-50 read-only:text-ink-muted',
        className,
      )}
      {...props}
    />
  ),
)
Textarea.displayName = 'Textarea'

export const Select = React.forwardRef<HTMLSelectElement, React.SelectHTMLAttributes<HTMLSelectElement>>(
  ({ className, children, ...props }, ref) => (
    <div className="relative">
      <select
        ref={ref}
        className={cn(
          'flex h-8 w-full appearance-none rounded-md border border-rule bg-card pl-3 pr-7 text-sm text-ink',
          'focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary',
          'disabled:bg-slate-50',
          className,
        )}
        {...props}
      >
        {children}
      </select>
      <ChevronDown className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" strokeWidth={2} />
    </div>
  ),
)
Select.displayName = 'Select'

export interface CheckboxProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: React.ReactNode
}
export const Checkbox = React.forwardRef<HTMLInputElement, CheckboxProps>(
  ({ id, label, className, ...props }, ref) => (
    <label htmlFor={id} className={cn('flex items-center gap-2 text-sm text-ink-muted cursor-pointer select-none', className)}>
      <input
        ref={ref}
        type="checkbox"
        id={id}
        className="h-4 w-4 rounded border-rule text-primary focus:ring-primary accent-blue-600"
        {...props}
      />
      {label}
    </label>
  ),
)
Checkbox.displayName = 'Checkbox'

/* Field labels */
export const Req = () => <span className="text-danger ml-0.5">*</span>

export interface FieldLabelProps {
  children: React.ReactNode
  required?: boolean
  tip?: string
  className?: string
}
export const FieldLabel = ({ children, required, tip, className }: FieldLabelProps) => (
  <div className={cn('flex items-center gap-1 text-sm font-medium text-ink-muted mb-1', className)}>
    {children}
    {required && <Req />}
    {tip && <Info className="w-3.5 h-3.5 text-ink-faint cursor-help" aria-label={tip} />}
  </div>
)

/* Field — label + input wrapper. Two-column form rows.
   `error` (string) renders an inline red validation message under the
   input and replaces the hint when both are present. */
export interface FieldProps {
  label: React.ReactNode
  required?: boolean
  tip?: string
  hint?: React.ReactNode
  error?: string | null
  children: React.ReactNode
  className?: string
}
export const Field = ({ label, required, tip, hint, error, children, className }: FieldProps) => (
  <div className={cn('flex flex-col gap-1', className)}>
    <FieldLabel required={required} tip={tip}>{label}</FieldLabel>
    {children}
    {error
      ? <p className="text-[11px] text-danger" role="alert">{error}</p>
      : hint && <p className="text-[11px] text-ink-faint">{hint}</p>}
  </div>
)

/* Info banner (yellow, used for policy reminders) */
export const InfoBanner = ({ children }: { children: React.ReactNode }) => (
  <div className="flex gap-3 rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
    <Info className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
    <div>{children}</div>
  </div>
)
