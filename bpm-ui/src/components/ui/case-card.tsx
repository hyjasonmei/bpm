import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'

/**
 * Mobile card rendering for table rows (`<md` viewports). Screens that show
 * a data table keep the `<table>` for `md:` and up, and render the same rows
 * through CaseCard below `md` — same data, same click handler, presentation
 * only. Layout contract: chips/status on top, title as the main line, ids
 * and timestamps as meta lines.
 */
export function CaseCard({ top, title, meta, onClick, className }: {
  /** Top row — TypeChip / status chips. */
  top?: ReactNode
  /** Main line — case title. */
  title: ReactNode
  /** Meta line(s) — case id, timestamps. */
  meta?: ReactNode
  onClick?: () => void
  className?: string
}) {
  const Tag = onClick ? 'button' : 'div'
  return (
    <Tag
      {...(onClick ? { type: 'button' as const, onClick } : {})}
      className={cn(
        'block w-full min-h-[44px] rounded-lg border border-slate-200 bg-card p-3 text-left',
        onClick && 'transition-colors hover:bg-slate-50/60 active:bg-slate-50',
        className,
      )}
    >
      {top && <div className="mb-1.5 flex flex-wrap items-center gap-1.5">{top}</div>}
      <div className="text-sm text-ink">{title}</div>
      {meta && <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-0.5 font-mono text-[11px] text-ink-muted">{meta}</div>}
    </Tag>
  )
}

/** Wrapper for a stack of CaseCards; also renders loading/error/empty copy. */
export function CaseCardList({ state, children, className }: {
  /** Pass a string to show a centered status line instead of cards. */
  state?: string | null
  children?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('space-y-2', className)}>
      {state
        ? <p className="py-8 text-center text-sm text-ink-faint">{state}</p>
        : children}
    </div>
  )
}
