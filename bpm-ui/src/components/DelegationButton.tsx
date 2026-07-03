import { useEffect, useRef, useState } from 'react'
import { UserCheck, Loader2, X, Check } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  getMyDelegation, setMyDelegation, clearMyDelegation, searchDelegationUsers,
  getPendingDelegationsForMe, acceptDelegation, declineDelegation,
  type MyDelegation, type DelegationUser, type PendingDelegation,
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
  const [incoming, setIncoming] = useState<PendingDelegation[]>([])
  const [results, setResults] = useState<DelegationUser[]>([])
  const [delegate, setDelegate] = useState('')
  const [delegateLabel, setDelegateLabel] = useState('')
  const [query, setQuery] = useState('')
  const [searching, setSearching] = useState(false)
  const [start, setStart] = useState(todayStr())
  const [end, setEnd] = useState(todayStr(7))
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    setLoading(true); setErr(null)
    Promise.all([getMyDelegation(), getPendingDelegationsForMe().catch(() => [])])
      .then(([cur, pend]) => {
        setCurrent(cur)
        setIncoming(pend)
        if (cur) {
          setDelegate(cur.delegateUserId)
          setDelegateLabel(cur.delegateName ?? cur.delegateUserId)
          setStart(cur.startAt.slice(0, 10)); setEnd(cur.endAt.slice(0, 10))
        }
      })
      .catch(e => setErr(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [open])

  async function respond(id: string, accept: boolean) {
    setBusy(true); setErr(null)
    try {
      if (accept) await acceptDelegation(id); else await declineDelegation(id)
      setIncoming(await getPendingDelegationsForMe().catch(() => []))
    } catch (e) { setErr(e instanceof Error ? e.message : String(e)) }
    finally { setBusy(false) }
  }

  // Server-side typeahead — query the directory (debounced) instead of loading
  // every user, so the picker scales to thousands of people.
  // Lightweight badge: check for incoming pending designations on mount so the
  // delegate sees the nudge without opening the panel.
  useEffect(() => {
    getPendingDelegationsForMe().then(setIncoming).catch(() => { /* silent */ })
  }, [])

  useEffect(() => {
    const s = query.trim()
    if (s === '') { setResults([]); setSearching(false); return }
    setSearching(true)
    let cancelled = false
    const t = setTimeout(() => {
      searchDelegationUsers(s)
        .then(r => { if (!cancelled) setResults(r) })
        .catch(() => { if (!cancelled) setResults([]) })
        .finally(() => { if (!cancelled) setSearching(false) })
    }, 250)
    return () => { cancelled = true; clearTimeout(t) }
  }, [query])

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
        {incoming.length > 0
          ? <span className="ml-0.5 inline-flex h-4 min-w-4 items-center justify-center rounded-full bg-blue-500 px-1 text-[10px] font-semibold text-white">{incoming.length}</span>
          : current?.activeNow && <span className="h-1.5 w-1.5 rounded-full bg-amber-400" />}
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
              {/* Incoming: someone designated ME — accept/decline */}
              {incoming.length > 0 && (
                <div className="space-y-2 rounded-md border border-blue-200 bg-blue-50 p-2.5">
                  <p className="text-[11px] font-semibold text-blue-900">待你回應的代理指定</p>
                  {incoming.map(d => (
                    <div key={d.id} className="rounded border border-blue-100 bg-white px-2.5 py-1.5 text-xs">
                      <div className="text-ink">
                        <span className="font-semibold">{d.delegatorName ?? '某位同事'}</span> 指定你為代理人
                        <br /><span className="text-ink-muted">{d.startAt.slice(0, 10)} ~ {d.endAt.slice(0, 10)}</span>
                      </div>
                      <div className="mt-1.5 flex gap-2">
                        <button onClick={() => respond(d.id, true)} disabled={busy}
                          className="inline-flex items-center gap-1 rounded bg-primary px-2 py-1 text-[11px] font-medium text-white hover:bg-blue-700 disabled:opacity-40">
                          <Check className="h-3 w-3" /> 接受
                        </button>
                        <button onClick={() => respond(d.id, false)} disabled={busy}
                          className="inline-flex items-center gap-1 rounded border border-rule px-2 py-1 text-[11px] text-ink-muted hover:bg-slate-50 disabled:opacity-40">
                          <X className="h-3 w-3" /> 拒絕
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {current && (
                <div className={cn('flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-xs',
                  current.activeNow ? 'border-amber-200 bg-amber-50 text-amber-800'
                    : current.status === 'Pending' ? 'border-blue-200 bg-blue-50 text-blue-800'
                    : 'border-rule bg-slate-50 text-ink-muted')}>
                  <span>
                    目前代理人：<span className="font-semibold">{current.delegateName ?? '—'}</span>
                    <br />{current.startAt.slice(0, 10)} ~ {current.endAt.slice(0, 10)}{' '}
                    {current.status === 'Pending' ? '（等待對方接受）'
                      : current.activeNow ? '（生效中）'
                      : current.status === 'Accepted' ? '（已接受・未到生效期）'
                      : '（未生效）'}
                  </span>
                  <button onClick={clear} disabled={busy} className="shrink-0 text-rose-600 hover:underline">取消</button>
                </div>
              )}

              <div className="text-xs text-ink-muted">
                代理人
                {delegate ? (
                  <div className="mt-1 flex items-center justify-between gap-2 rounded-md border border-rule bg-slate-50 px-2.5 py-1.5 text-sm text-ink">
                    <span className="truncate">{delegateLabel}</span>
                    <button onClick={() => { setDelegate(''); setDelegateLabel(''); setQuery('') }}
                      className="shrink-0 text-ink-faint hover:text-rose-600" title="更換"><X className="h-3.5 w-3.5" /></button>
                  </div>
                ) : (
                  <div className="relative">
                    <input
                      value={query}
                      onChange={e => { setQuery(e.target.value); setErr(null) }}
                      placeholder="搜尋姓名或 email…"
                      className="mt-1 w-full rounded-md border border-rule bg-card px-2.5 py-1.5 text-sm text-ink placeholder:text-ink-faint focus:outline-none focus:ring-1 focus:ring-primary"
                    />
                    {query.trim() !== '' && (
                      <div className="absolute z-10 mt-1 max-h-44 w-full overflow-auto rounded-md border border-rule bg-card shadow-lg">
                        {searching ? (
                          <div className="px-3 py-2 text-xs text-ink-faint"><Loader2 className="inline h-3 w-3 animate-spin" /> 搜尋中…</div>
                        ) : results.length === 0 ? (
                          <div className="px-3 py-2 text-xs text-ink-faint">找不到符合的人</div>
                        ) : results.map(u => (
                          <button key={u.userId} type="button"
                            onClick={() => { setDelegate(u.userId); setDelegateLabel(`${u.name}${u.email ? ` · ${u.email}` : ''}`); setQuery('') }}
                            className="flex w-full flex-col items-start px-3 py-1.5 text-left hover:bg-slate-50">
                            <span className="text-sm text-ink">{u.name}</span>
                            {u.email && <span className="text-[11px] text-ink-faint">{u.email}</span>}
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                )}
              </div>

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
