import { useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Loader2 } from 'lucide-react'
import { Button, type ButtonProps } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { cn } from '@/lib/cn'

/** Per-action confirm modal copy. Pass `false` to opt OUT (rare). */
export interface ActionConfirm {
  title?: string
  titleZh?: string
  description?: string
  confirmText?: string
  tone?: 'danger' | 'default'
}

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
  /**
   * Confirm modal before firing onClick. **Default ON** — every action gets a
   * confirm step (product requirement). Pass an object to tailor the copy, or
   * `false` to opt out for a genuinely safe action.
   */
  confirm?: ActionConfirm | false
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
 *
 * PR-X12: switched from `sticky bottom-0` to `fixed bottom-0` so the
 * bar is always pinned to the viewport edge, not the bottom of the
 * content. Previously short pages would render the bar inline below
 * the content (sticky has no effect until the page can scroll), which
 * read as "below the content" rather than "fixed at the bottom" —
 * this was flagged as a regression. Inner row stays centred via
 * `max-w-screen-2xl` to line up with the header.
 *
 * PR-X13: render through a portal to `document.body`. The host
 * `AppLayout`'s `<main>` carries the `.fade-in` entrance animation,
 * whose keyframes reference `transform` with `fill-mode: both`. An
 * in-effect transform animation makes that ancestor a *containing
 * block* for `position: fixed` descendants, so the bar was pinning to
 * the bottom of `<main>`'s content (i.e. below the fold) rather than
 * the viewport. Portaling to <body> escapes any such ancestor so
 * `fixed` truly means viewport-fixed.
 */
export function ActionFooter({ actions, hint, className }: ActionFooterProps) {
  // Pending = the action awaiting its confirm modal. Every action confirms by
  // default (product requirement); `confirm: false` opts a safe one out.
  const [pending, setPending] = useState<ActionFooterItem | null>(null)
  if (actions.length === 0) return null

  const onButton = (a: ActionFooterItem) => {
    if (a.confirm === false) { void a.onClick(); return }
    setPending(a)
  }

  const c = pending && pending.confirm !== false ? (pending.confirm ?? {}) : null
  const labelText = typeof pending?.label === 'string' ? pending.label : '此操作'

  return (
    <>
      {createPortal(
        <div
          className={cn(
            'no-print fixed inset-x-0 bottom-0 z-30 border-t border-rule bg-card/95 shadow-[0_-4px_12px_-8px_rgba(15,23,42,0.18)] backdrop-blur',
            className,
          )}
        >
          <div className="mx-auto flex max-w-screen-2xl items-center gap-3 px-4 py-3">
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
                  onClick={() => onButton(a)}
                >
                  {a.pending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                  {a.label}
                </Button>
              ))}
            </div>
          </div>
        </div>,
        document.body,
      )}
      {pending && c && (
        <ConfirmDialog
          open
          title={c.title ?? 'Confirm action'}
          titleZh={c.titleZh ?? `確認${labelText}？`}
          description={c.description}
          confirmText={c.confirmText ?? '確認'}
          tone={c.tone ?? (pending.variant === 'destructive' ? 'danger' : 'default')}
          onConfirm={() => { const item = pending; setPending(null); void item.onClick() }}
          onCancel={() => setPending(null)}
        />
      )}
    </>
  )
}
