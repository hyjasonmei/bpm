import { useCallback, useEffect, useRef, useState } from 'react'
import { Bell, CheckCheck } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { cn } from '@/lib/cn'
import {
  fetchMyNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type NotificationDto,
} from '@/lib/api/notifications'

const POLL_MS = 30_000

interface Props {
  /** Optional seed; the menu owns its own live count via polling. */
  unreadCount?: number
}

export function NotificationsMenu(_props: Props) {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [items, setItems] = useState<NotificationDto[]>([])
  const [unread, setUnread] = useState(0)
  const [loading, setLoading] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  const refresh = useCallback(async () => {
    try {
      const res = await fetchMyNotifications(20)
      setItems(res.items)
      setUnread(res.unreadCount)
    } catch {
      // Silent — a transient inbox/notify hiccup shouldn't break the shell.
    }
  }, [])

  // Initial load + background poll.
  useEffect(() => {
    void refresh()
    const t = setInterval(() => void refresh(), POLL_MS)
    return () => clearInterval(t)
  }, [refresh])

  // Refresh when the panel is opened so it's never stale on view.
  useEffect(() => {
    if (open) {
      setLoading(true)
      void refresh().finally(() => setLoading(false))
    }
  }, [open, refresh])

  // Close on outside click / Escape.
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

  const onItemClick = useCallback(async (n: NotificationDto) => {
    setOpen(false)
    if (!n.isRead) {
      setItems(prev => prev.map(i => i.id === n.id ? { ...i, isRead: true } : i))
      setUnread(u => Math.max(0, u - 1))
      try { await markNotificationRead(n.id) } catch { void refresh() }
    }
    if (n.link) navigate(n.link)
  }, [navigate, refresh])

  const onMarkAll = useCallback(async () => {
    setItems(prev => prev.map(i => ({ ...i, isRead: true })))
    setUnread(0)
    try { await markAllNotificationsRead() } catch { void refresh() }
  }, [refresh])

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
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[9px] font-bold leading-none text-white shadow-sm">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>
      {open && (
        // <md: fixed panel spanning the viewport (the 384px flyout clipped
        // off-screen when anchored to the bell); md+: original flyout.
        <div className="fixed inset-x-2 top-14 z-30 origin-top overflow-hidden rounded-lg border border-rule bg-card text-ink shadow-2xl md:absolute md:inset-x-auto md:right-0 md:top-[calc(100%+6px)] md:w-96 md:origin-top-right">
          <div className="flex items-center justify-between border-b border-rule px-4 py-2.5">
            <p className="text-sm font-semibold">通知 / Notifications</p>
            {unread > 0 && (
              <button
                onClick={onMarkAll}
                className="inline-flex items-center gap-1 text-[11px] text-primary hover:underline"
              >
                <CheckCheck className="h-3 w-3" /> 全部標為已讀
              </button>
            )}
          </div>

          <div className="max-h-96 overflow-y-auto">
            {loading && items.length === 0 ? (
              <div className="px-4 py-8 text-center text-sm text-ink-muted">載入中…</div>
            ) : items.length === 0 ? (
              <div className="px-4 py-8 text-center text-sm text-ink-muted">
                <Bell className="mx-auto mb-2 h-6 w-6 text-ink-faint" />
                目前沒有通知
              </div>
            ) : (
              <ul className="divide-y divide-slate-100">
                {items.map(n => (
                  <li key={n.id}>
                    <button
                      onClick={() => onItemClick(n)}
                      className={cn(
                        'flex w-full items-start gap-2.5 px-4 py-3 text-left transition-colors hover:bg-slate-50/70',
                        !n.isRead && 'bg-amber-50/40',
                      )}
                    >
                      <span className={cn(
                        'mt-1.5 block h-1.5 w-1.5 shrink-0 rounded-full',
                        n.isRead ? 'bg-transparent' : 'bg-amber-500',
                      )} />
                      <span className="min-w-0 flex-1">
                        <span className={cn('block truncate text-[13px]', n.isRead ? 'text-ink-muted' : 'font-semibold text-ink')}>
                          {n.title}
                        </span>
                        {n.body && (
                          <span className="mt-0.5 block truncate text-[11.5px] text-ink-muted">{firstLine(n.body)}</span>
                        )}
                        <span className="mt-0.5 block text-[10.5px] text-ink-faint">{humanAgo(n.createdAt)}</span>
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function firstLine(body: string): string {
  const line = body.split('\n').map(s => s.trim()).find(Boolean) ?? ''
  return line
}

function humanAgo(iso: string): string {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return iso
  const mins = Math.round((Date.now() - then) / 60_000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.round(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  const days = Math.round(hrs / 24)
  return `${days}d ago`
}
