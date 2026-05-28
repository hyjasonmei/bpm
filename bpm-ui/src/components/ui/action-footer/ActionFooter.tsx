import type { ReactNode } from 'react'
import { Loader2 } from 'lucide-react'
import { Button, type ButtonProps } from '@/components/ui/button'
import { cn } from '@/lib/cn'

export interface ActionFooterItem {
  id: string
  label: ReactNode
  /** Maps to Button's `variant`. Default = 'primary'. */
  variant?: ButtonProps['variant']
  disabled?: boolean
  /** When true, render a spinner inside the button and force-disable it. */
  pending?: boolean
  /** Inline title attribute for hover tooltip (validation hint etc). */
  title?: string
  onClick: () => void | Promise<void>
}

export interface ActionFooterProps {
  actions: ActionFooterItem[]
  /** Optional left-aligned status hint. Reserved for future SLA countdown
   *  or pending-state copy; chef may pass anything renderable. */
  hint?: ReactNode
  /** Extra class on the outer container — typically not needed. */
  className?: string
}

/**
 * Sticky-bottom action bar for case detail / task screens.
 *
 * Per-flow CaseDetail components (chef-cooked, `features/<CODE>/V<N>/`)
 * MUST surface their decision buttons via this primitive instead of
 * inline buttons — keeps the visual contract uniform across flows and
 * leaves room for a future shared SLA indicator on the left.
 *
 *   <ActionFooter
 *     hint={"等候主管核准"}
 *     actions={[
 *       { id: 'reject',  label: '退件',          variant: 'destructive', onClick: ... },
 *       { id: 'approve', label: '核准 / Approve', variant: 'primary',     onClick: ... },
 *     ]}
 *   />
 *
 * Render once at the bottom of the page; the host page should add
 * `pb-24` (or similar) on its scroll container so the footer doesn't
 * cover real content.
 */
export function ActionFooter({ actions, hint, className }: ActionFooterProps) {
  if (actions.length === 0) return null
  return (
    <div
      className={cn(
        'sticky bottom-0 z-20 -mx-4 mt-4 flex items-center gap-3 border-t border-rule bg-card/95 px-4 py-3 shadow-[0_-4px_12px_-8px_rgba(15,23,42,0.18)] backdrop-blur',
        className,
      )}
    >
      <div className="min-w-0 flex-1 text-xs text-ink-muted">
        {hint}
      </div>
      <div className="flex flex-wrap items-center justify-end gap-2">
        {actions.map(a => (
          <Button
            key={a.id}
            variant={a.variant ?? 'primary'}
            size="md"
            disabled={a.disabled || a.pending}
            title={a.title}
            onClick={() => { void a.onClick() }}
          >
            {a.pending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            {a.label}
          </Button>
        ))}
      </div>
    </div>
  )
}
