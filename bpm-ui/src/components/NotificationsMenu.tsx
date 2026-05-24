import { useEffect, useRef, useState } from 'react'
import { Bell } from 'lucide-react'
import { cn } from '@/lib/cn'

interface Props {
  /** When connected to the notification engine this becomes the real
   *  unread count; today it defaults to 0 since no backend feed exists
   *  on bpm-svc yet (see add-notification-engine openspec change). */
  unreadCount?: number
}

export function NotificationsMenu({ unreadCount = 0 }: Props) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onDocClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onEsc)
    }
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        title="Notifications"
        onClick={() => setOpen(o => !o)}
        className={cn(
          'relative flex h-8 w-8 items-center justify-center rounded text-white/70 transition-colors hover:bg-white/10 hover:text-white',
          open && 'bg-white/10 text-white',
        )}
      >
        <Bell className="h-4 w-4" />
        {unreadCount > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[9px] font-bold leading-none text-white shadow-sm">
            {unreadCount > 9 ? '9+' : unreadCount}
          </span>
        )}
      </button>
      {open && (
        <div className="absolute right-0 top-[calc(100%+6px)] z-30 w-80 origin-top-right rounded-lg border border-rule bg-card text-ink shadow-2xl">
          <div className="flex items-center justify-between border-b border-rule px-4 py-2.5">
            <p className="text-sm font-semibold">通知 / Notifications</p>
          </div>
          <div className="px-4 py-8 text-center text-sm text-ink-muted">
            <Bell className="mx-auto mb-2 h-6 w-6 text-ink-faint" />
            目前沒有未讀通知
            <p className="mt-1 text-[11px] text-ink-faint">通知中心將於 add-notification-engine 上線</p>
          </div>
        </div>
      )}
    </div>
  )
}
