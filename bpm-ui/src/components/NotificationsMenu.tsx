import { useEffect, useRef, useState } from 'react'
import { Bell, Check, AlertCircle, Info, FileText } from 'lucide-react'
import { cn } from '@/lib/cn'

interface MockNotification {
  id: string
  kind: 'approved' | 'returned' | 'assigned' | 'reminder' | 'system'
  title: string
  body: string
  ago: string
  read: boolean
}

const MOCK: MockNotification[] = [
  { id: 'n1', kind: 'approved',  title: 'Elton Yang approved your LEAVE request',     body: 'TW-LEAVE-26-000044 · 2 days annual leave',                ago: '15m ago', read: false },
  { id: 'n2', kind: 'assigned',  title: 'New approval awaiting your action',          body: 'TW-RESIGN-26-000003 · Wilson You · 主管簽核',              ago: '1h ago',  read: false },
  { id: 'n3', kind: 'returned',  title: 'Jean Hsu returned your GEV request',         body: 'TW-GEV-26-000891 · 缺廠商發票檔案，請補件',                  ago: '5h ago',  read: false },
  { id: 'n4', kind: 'reminder',  title: 'Reminder: APE return due 2026/05/01',        body: 'APE-26-000040 · NTD 5,000 預支費用尚未結算',                ago: '1d ago',  read: true  },
  { id: 'n5', kind: 'system',    title: 'New flow available: Department Transfer',    body: 'HR 加入「部門異動」流程，可從 Create → DEPTX 申請',         ago: '2d ago',  read: true  },
]

interface Props {
  unreadCount?: number
}

export function NotificationsMenu({ unreadCount }: Props) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const count = unreadCount ?? MOCK.filter(n => !n.read).length

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
        {count > 0 && (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-500 px-1 text-[9px] font-bold leading-none text-white shadow-sm">
            {count > 9 ? '9+' : count}
          </span>
        )}
      </button>
      {open && (
        <div className="absolute right-0 top-[calc(100%+6px)] z-30 w-96 origin-top-right rounded-lg border border-rule bg-card text-ink shadow-2xl">
          <div className="flex items-center justify-between border-b border-rule px-4 py-2.5">
            <p className="text-sm font-semibold">Notifications</p>
            <button className="text-xs text-primary hover:underline">Mark all read</button>
          </div>
          <div className="max-h-96 overflow-y-auto">
            {MOCK.map(n => (
              <div key={n.id} className={cn('flex items-start gap-3 border-b border-rule px-4 py-3 last:border-b-0', !n.read && 'bg-blue-50/40')}>
                <span className={cn('mt-0.5 inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full', toneClass(n.kind))}>
                  {iconFor(n.kind)}
                </span>
                <div className="min-w-0 flex-1">
                  <p className={cn('text-sm leading-snug', !n.read ? 'font-semibold text-ink' : 'text-ink-muted')}>{n.title}</p>
                  <p className="mt-0.5 text-xs text-ink-muted">{n.body}</p>
                  <p className="mt-1 text-[10px] uppercase tracking-wider text-ink-faint">{n.ago}</p>
                </div>
              </div>
            ))}
          </div>
          <div className="border-t border-rule bg-slate-50/50 px-4 py-2 text-center text-[11px] text-ink-faint">
            Mock data — wired to /api/notifications when notification engine ships
          </div>
        </div>
      )}
    </div>
  )
}

function toneClass(kind: MockNotification['kind']): string {
  switch (kind) {
    case 'approved': return 'bg-green-100 text-green-700'
    case 'returned': return 'bg-amber-100 text-amber-700'
    case 'assigned': return 'bg-blue-100 text-blue-700'
    case 'reminder': return 'bg-purple-100 text-purple-700'
    case 'system':   return 'bg-slate-100 text-slate-600'
  }
}

function iconFor(kind: MockNotification['kind']) {
  switch (kind) {
    case 'approved': return <Check className="h-3.5 w-3.5" />
    case 'returned': return <AlertCircle className="h-3.5 w-3.5" />
    case 'assigned': return <FileText className="h-3.5 w-3.5" />
    case 'reminder': return <Bell className="h-3.5 w-3.5" />
    case 'system':   return <Info className="h-3.5 w-3.5" />
  }
}
