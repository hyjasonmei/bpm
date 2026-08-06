import * as React from 'react'
import { cn } from '@/lib/cn'

// `print-block` / `print-title` are no-ops on screen — they give the
// shared print layer (index.css `@media print`) a stable hook so a card
// prints as a flat frame and never breaks across two pages, without
// keying off Tailwind utility names.
export const SectionCard = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('print-block overflow-hidden rounded-lg border border-rule bg-card', className)} {...props} />
)

export const SectionTitle = ({ className, children, right }: React.HTMLAttributes<HTMLDivElement> & { right?: React.ReactNode }) => (
  <div className={cn('print-title flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2.5 text-sm font-semibold text-ink', className)}>
    <span>{children}</span>
    {right}
  </div>
)

export const Card = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('rounded-lg border border-rule bg-card p-5', className)} {...props} />
)
