import * as React from 'react'
import { cn } from '@/lib/cn'

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, ...props }, ref) => (
    <input
      ref={ref}
      className={cn(
        'h-8 w-full rounded-md border border-rule bg-card px-2.5 text-sm text-ink',
        'placeholder:text-ink-faint transition-colors',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
        'disabled:opacity-40 disabled:pointer-events-none',
        className,
      )}
      {...props}
    />
  ),
)
Input.displayName = 'Input'
