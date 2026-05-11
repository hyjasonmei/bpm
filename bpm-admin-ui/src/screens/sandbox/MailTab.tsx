import { useEffect, useMemo, useState } from 'react'
import { X, RefreshCw, CheckCheck } from 'lucide-react'
import { listCaptured, getCaptured, markCapturedRead } from '@/lib/api/sandbox'
import { SandboxChannel, type CapturedMessageSummaryDto, type CapturedMessageDetailDto } from '@/types/sandbox'

export function MailTab() {
  return (
    <CapturedListWithModal
      channel={SandboxChannel.Email}
      emptyLabel="No captured emails yet."
      columns={[
        { header: 'Subject', render: r => r.subject ?? <em className="text-ink-faint">(no subject)</em> },
      ]}
      DetailRender={EmailDetail}
    />
  )
}

function EmailDetail({ d }: { d: CapturedMessageDetailDto }) {
  return (
    <div className="space-y-3">
      <KV label="Subject" value={d.subject ?? '—'} />
      <KV label="Intended recipients" value={d.intendedRecipients.join(', ') || '—'} mono />
      <KV label="Originating notification" value={d.originatingNotificationId ?? '—'} mono />
      <KV label="Process instance" value={d.processInstanceId ?? '—'} mono />
      <div>
        <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint mb-1">HTML body</p>
        {d.bodyHtml
          ? <iframe
              title="captured-html"
              srcDoc={d.bodyHtml}
              sandbox=""
              className="h-72 w-full rounded border border-rule bg-white"
            />
          : <p className="text-xs text-ink-muted">— no HTML body —</p>}
      </div>
      {d.bodyText && (
        <div>
          <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint mb-1">Plain-text body</p>
          <pre className="max-h-60 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px]">{d.bodyText}</pre>
        </div>
      )}
    </div>
  )
}

/* ─── shared list-with-modal scaffold (reused by Webhooks/SMS) ─── */

interface Column {
  header: string
  render: (row: CapturedMessageSummaryDto) => React.ReactNode
}

interface ScaffoldProps {
  channel: number
  emptyLabel: string
  columns: Column[]
  DetailRender: (props: { d: CapturedMessageDetailDto }) => React.ReactElement
}

export function CapturedListWithModal({ channel, emptyLabel, columns, DetailRender }: ScaffoldProps) {
  const [rows, setRows] = useState<CapturedMessageSummaryDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [recipientFilter, setRecipientFilter] = useState<string>('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    let cancelled = false
    setRows(null); setError(null)
    listCaptured({ channel: channel as 1 | 2 | 3, unread: unreadOnly, limit: 100 })
      .then(r => { if (!cancelled) setRows(r) })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [channel, unreadOnly, reloadKey])

  const recipientOptions = useMemo(() => {
    // We don't have recipients in the summary — derive from /by-id fetches.
    // v1 leaves this as a free-text filter that searches the subject/event.
    return [] as string[]
  }, [rows])

  const filtered = useMemo(() => {
    if (!rows) return rows
    if (!recipientFilter) return rows
    const needle = recipientFilter.toLowerCase()
    return rows.filter(r =>
      (r.subject ?? '').toLowerCase().includes(needle)
      || (r.eventType ?? '').toLowerCase().includes(needle)
    )
  }, [rows, recipientFilter])

  async function handleMarkAllRead() {
    if (!rows) return
    const unread = rows.filter(r => !r.readByMe)
    for (const r of unread) {
      try { await markCapturedRead(r.id) } catch { /* keep going */ }
    }
    setReloadKey(k => k + 1)
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-3">
        <label className="inline-flex items-center gap-1.5 text-xs text-ink-muted">
          <input
            type="checkbox"
            checked={unreadOnly}
            onChange={e => setUnreadOnly(e.target.checked)}
            className="h-3.5 w-3.5"
          />
          Unread only
        </label>
        <input
          type="text"
          value={recipientFilter}
          onChange={e => setRecipientFilter(e.target.value)}
          placeholder="Filter subject / event…"
          className="h-7 w-56 rounded border border-rule px-2 text-xs"
        />
        <button
          onClick={() => setReloadKey(k => k + 1)}
          className="inline-flex items-center gap-1 rounded border border-rule px-2 py-1 text-xs hover:bg-slate-50"
        >
          <RefreshCw className="h-3 w-3" /> Refresh
        </button>
        <button
          onClick={handleMarkAllRead}
          disabled={!rows || rows.every(r => r.readByMe)}
          className="inline-flex items-center gap-1 rounded border border-rule px-2 py-1 text-xs hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
        >
          <CheckCheck className="h-3 w-3" /> Mark all as read
        </button>
        {recipientOptions.length > 0 && <span className="text-[10px] text-ink-faint">{recipientOptions.length} recipients</span>}
      </div>

      {error && (
        <div className="rounded border border-rose-200 bg-rose-50 p-2 text-xs text-rose-800">
          Failed to load: {error}
        </div>
      )}
      {!rows && !error && <p className="text-xs text-ink-muted">Loading…</p>}
      {rows && filtered && filtered.length === 0 && (
        <p className="text-xs text-ink-muted">{emptyLabel}</p>
      )}
      {filtered && filtered.length > 0 && (
        <div className="overflow-hidden rounded border border-rule">
          <table className="w-full text-xs">
            <thead className="bg-slate-50 text-left">
              <tr>
                <th className="px-3 py-1.5 font-semibold text-ink-muted w-36">Captured at</th>
                {columns.map(c => <th key={c.header} className="px-3 py-1.5 font-semibold text-ink-muted">{c.header}</th>)}
                <th className="px-3 py-1.5 font-semibold text-ink-muted w-20 text-right">Read</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-rule">
              {filtered.map(r => (
                <tr
                  key={r.id}
                  onClick={() => setSelectedId(r.id)}
                  className="cursor-pointer hover:bg-slate-50"
                >
                  <td className="px-3 py-1.5 text-ink-muted">{formatRelative(r.capturedAt)}</td>
                  {columns.map(c => <td key={c.header} className="px-3 py-1.5 text-ink">{c.render(r)}</td>)}
                  <td className="px-3 py-1.5 text-right">
                    {r.readByMe
                      ? <span className="text-[10px] text-ink-faint">✓</span>
                      : <span className="inline-flex h-1.5 w-1.5 rounded-full bg-rose-500" />}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {selectedId && (
        <DetailModal
          id={selectedId}
          onClose={() => { setSelectedId(null); setReloadKey(k => k + 1) }}
          DetailRender={DetailRender}
        />
      )}
    </div>
  )
}

function DetailModal({ id, onClose, DetailRender }: { id: string; onClose: () => void; DetailRender: (props: { d: CapturedMessageDetailDto }) => React.ReactElement }) {
  const [d, setD] = useState<CapturedMessageDetailDto | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setD(null); setErr(null)
    getCaptured(id)
      .then(detail => {
        if (cancelled) return
        setD(detail)
        // Fire-and-forget mark-as-read so subsequent list reloads reflect it.
        if (!detail.readByMe) markCapturedRead(id).catch(() => { /* ignore */ })
      })
      .catch(e => { if (!cancelled) setErr(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [id])

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div className="flex max-h-[90vh] w-full max-w-3xl flex-col rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-rule px-5 py-3">
          <h3 className="text-base font-bold text-ink">Captured message</h3>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-auto p-5">
          {err && <p className="text-xs text-rose-700">Failed to load: {err}</p>}
          {!d && !err && <p className="text-xs text-ink-muted">Loading…</p>}
          {d && <DetailRender d={d} />}
        </div>
      </div>
    </div>
  )
}

function KV({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">{label}</p>
      <p className={mono ? 'font-mono text-[11.5px] text-ink break-all' : 'text-sm text-ink'}>{value}</p>
    </div>
  )
}

function formatRelative(iso: string): string {
  const then = new Date(iso).getTime()
  const now = Date.now()
  const diff = Math.max(0, now - then)
  const secs = Math.floor(diff / 1000)
  if (secs < 60) return `${secs}s ago`
  const mins = Math.floor(secs / 60)
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}
