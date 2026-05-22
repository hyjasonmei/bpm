import { useEffect, useRef, useState } from 'react'
import { ChevronDown, LogOut, User } from 'lucide-react'
import { cn } from '@/lib/cn'
import { useAuth } from '@/flowcook/auth/useAuth'

export function UserMenu() {
  const { user, logout } = useAuth()
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

  const displayName = user?.displayName ?? 'admin'

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
        className={cn(
          'inline-flex items-center gap-1.5 rounded border bg-card px-2.5 py-1 text-xs font-medium transition-colors',
          open ? 'border-primary text-primary' : 'border-rule text-ink-muted hover:border-primary hover:text-primary',
        )}
      >
        <User className="h-3.5 w-3.5" />
        <span className="max-w-[120px] truncate">{displayName}</span>
        <ChevronDown className={cn('h-3 w-3 transition-transform', open && 'rotate-180')} />
      </button>
      {open && (
        <div
          role="menu"
          className="absolute right-0 z-30 mt-1 w-48 overflow-hidden rounded-md border border-rule bg-white py-1 shadow-lg"
        >
          <div className="px-3 pb-1 pt-0.5">
            <div className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">
              signed in as
            </div>
            <div className="mt-0.5 truncate text-xs font-semibold text-ink">{displayName}</div>
          </div>
          <div className="my-1 border-t border-rule" />
          <button
            role="menuitem"
            onClick={() => {
              setOpen(false)
              void logout()
            }}
            className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-xs text-ink hover:bg-bg"
          >
            <LogOut className="h-3.5 w-3.5 shrink-0" />
            <span>Sign out</span>
          </button>
        </div>
      )}
    </div>
  )
}
