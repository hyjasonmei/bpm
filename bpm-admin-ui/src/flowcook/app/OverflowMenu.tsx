import { useEffect, useRef, useState, type ReactNode } from 'react'
import { MoreVertical } from 'lucide-react'
import { cn } from '@/lib/cn'

export interface OverflowItem {
  id: string
  label: string
  icon?: ReactNode
  /** "productive" (default) renders as ink, "risky" renders muted, "danger" renders red. */
  tone?: 'productive' | 'risky' | 'danger'
  disabled?: boolean
  hint?: string
  onClick: () => void
}

export interface OverflowGroup {
  label?: string
  items: OverflowItem[]
}

export function OverflowMenu({
  groups,
  align = 'right',
  buttonTitle = 'More actions',
}: {
  groups: OverflowGroup[]
  align?: 'left' | 'right'
  buttonTitle?: string
}) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    function onDown(e: MouseEvent) {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  // Hide empty groups so the menu doesn't render empty dividers when all
  // items in a group are state-gated off.
  const visibleGroups = groups.filter(g => g.items.length > 0)
  if (visibleGroups.length === 0) return null

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        title={buttonTitle}
        aria-haspopup="menu"
        aria-expanded={open}
        className="flex h-7 w-7 items-center justify-center rounded border border-rule bg-card text-ink-muted transition-colors hover:border-primary hover:text-primary"
      >
        <MoreVertical className="h-3.5 w-3.5" />
      </button>
      {open && (
        <div
          role="menu"
          className={cn(
            'absolute z-30 mt-1 w-56 overflow-hidden rounded-md border border-rule bg-white py-1 shadow-lg',
            align === 'right' ? 'right-0' : 'left-0',
          )}
        >
          {visibleGroups.map((g, gi) => (
            <div key={gi}>
              {gi > 0 && <div className="my-1 border-t border-rule" />}
              {g.label && (
                <div className="px-3 pb-0.5 pt-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">
                  {g.label}
                </div>
              )}
              {g.items.map(item => (
                <button
                  key={item.id}
                  role="menuitem"
                  disabled={item.disabled}
                  onClick={() => {
                    setOpen(false)
                    if (!item.disabled) item.onClick()
                  }}
                  title={item.hint}
                  className={cn(
                    'flex w-full items-center gap-2 px-3 py-1.5 text-left text-xs transition-colors',
                    item.disabled && 'cursor-not-allowed text-ink-faint',
                    !item.disabled && item.tone === 'danger' && 'text-danger hover:bg-danger/5',
                    !item.disabled && item.tone === 'risky' && 'text-ink-muted hover:bg-warn/5 hover:text-warn',
                    !item.disabled && (!item.tone || item.tone === 'productive') && 'text-ink hover:bg-bg',
                  )}
                >
                  {item.icon && <span className="flex h-3.5 w-3.5 shrink-0 items-center justify-center">{item.icon}</span>}
                  <span className="flex-1">{item.label}</span>
                </button>
              ))}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
