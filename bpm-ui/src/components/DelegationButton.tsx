import { useEffect, useRef, useState } from 'react'
import { UserCheck, Loader2, X, Check } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  getMyDelegation, setMyDelegation, clearMyDelegation, getDelegationUsers,
  type MyDelegation, type DelegationUser,
} from '@/lib/api/delegation'

function todayStr(offsetDays = 0): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  return d.toISOString().slice(0, 10)
}

/**
 * Header control (left of the account menu): set a 代理人 (delegate) + date range.
 * While active, the delegate sees and can act on this user's pending tasks.
 */
export function DelegationButton() {
  const [open, setOpen] = useState(false)
  const [current, setCurrent] = useState<MyDelegation | null>(null)
  const [users, setUsers] = useState<DelegationUser[]>([])
  const [delegate, setDelegate] = useState('')
  const [start, setStart] = useState(todayStr())
  const [end, setEnd] = useState(todayStr(7))
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    setLoading(true); setErr(null)
    Promise.all([getMyDelegation(), getDelegationUsers()])
      .then(([cur, us]) => {
        setCurrent(cur); setUsers(us)
        if (cur) { setDelegate(cur.delegateUserId); setStart(cur.startAt.slice(0, 10)); setEnd(cur.endAt.slice(0, 10)) }
      })
      .catch(e => setErr(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [open])

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false) }
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onClick); document.addEventListener('keydown', onEsc)
    return () => { document.removeEventListener('mousedown', onClick); document.removeEventListener('keydown', onEsc) }
  }, [open])

  async function save() {
    if (!delegate) { setErr('請選擇代理人'); return }
    setBusy(true); setErr(null)
    try {
      await setMyDelegation(delegate, `${start}T00:00:00Z`, `${end}T23:59:59Z`)
      setCurrent(await getMyDelegation())
    } catch (e) { setErr(e instanceof Error ? e.message : String(e)) }
    finally { setBusy(false) }
  }

  async function clear() {
    setBusy(true); setErr(null)
    try { await clearMyDelegation(); setCurrent(null); setDelegate('') }
    catch (e) { setErr(e instanceof Error ? e.message : String(e)) }
    finally { setBusy(false) }
  }

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(o => !o)}
        title="代理人設定"
        className={cn(
          'inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-white/90 transition-colors hover:bg-white/10',
          open && 'bg-white/10',
          current?.activeNow && 'text-amber-300',
        )}
      >
        <UserCheck className="h-4 w-4" />
        <span className="hidden md:inline">代理人</span>
        {current?.activeNow && <span className="h-1.5 w-1.5 rounded-full bg-amber-400" />}
      </button>

      {open && (
        <div className="absolute right-0 top-[calc(100%+6px)] z-40 w-80 origin-top-right rounded-lg border border-rule bg-card text-ink shadow-2xl">
          <div className="border-b border-rule px-4 py-2.5">
            <p className="text-sm font-semibold">代理人設定</p>
            <p className="text-[11px] text-ink-muted">設定期間內，代理人能看到並代你簽核待辦。</p>
          </div>

          {loading ? (
            <div className="p-4 text-sm text-ink-faint"><Loader2 className="inline h-4 w-4 animate-spin" /> 載入中…</div>
          ) : (
            <div className="space-y-3 p-4">
              {current && (
                <div className={cn('flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-xs',
                  current.activeNow ? 'border-amber-200 bg-amber-50 text-amber-800' : 'border-rule bg-slate-50 text-ink-muted')}>
                  <span>
                    目前代理人：<span className="font-semibold">{current.delegateName ?? '—'}</span>
                    <br />{current.startAt.slice(0, 10)} ~ {current.endAt.slice(0, 10)} {current.activeNow ? '（生效中）' : '（未生效）'}
                  </span>
                  <button onClick={clear} disabled={busy} className="shrink-0 text-rose-600 hover:underline">取消</button>
                </div>
              )}

              <label className="block text-xs text-ink-muted">
                代理人
                <select value={delegate} onChange={e => { setDelegate(e.target.value); setErr(null) }}
                  className="mt-1 w-full rounded-md border border-rule bg-card px-2.5 py-1.5 text-sm text-ink focus:outline-none focus:ring-1 focus:ring-primary">
                  <option value="">選擇帳號…</option>
                  {users.map(u => <option key={u.userId} value={u.userId}>{u.name}{u.email ? ` · ${u.email}` : ''}</option>)}
                </select>
              </label>

              <div className="flex gap-2">
                <label className="flex-1 text-xs text-ink-muted">起<input type="date" value={start} onChange={e => setStart(e.target.value)}
                  className="mt-1 w-full rounded-md border border-rule bg-card px-2 py-1.5 text-sm text-ink" /></label>
                <label className="flex-1 text-xs text-ink-muted">迄<input type="date" value={end} onChange={e => setEnd(e.target.value)}
                  className="mt-1 w-full rounded-md border border-rule bg-card px-2 py-1.5 text-sm text-ink" /></label>
              </div>

              {err && <p className="text-[11px] text-rose-700"><X className="inline h-3 w-3" /> {err}</p>}

              <button onClick={save} disabled={busy || !delegate}
                className="inline-flex w-full items-center justify-center gap-1.5 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-40">
                {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} 儲存
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
