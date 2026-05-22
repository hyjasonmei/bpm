import * as React from 'react'
import { cn } from '@/lib/cn'

export const SectionCard = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('overflow-hidden rounded-lg border border-rule bg-card', className)} {...props} />
)

export const SectionTitle = ({ className, children, right }: React.HTMLAttributes<HTMLDivElement> & { right?: React.ReactNode }) => (
  <div className={cn('flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2.5 text-sm font-semibold text-ink', className)}>
    <span>{children}</span>
    {right}
  </div>
)

export const Card = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('rounded-lg border border-rule bg-card p-5', className)} {...props} />
)
