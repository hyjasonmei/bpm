/**
 * Read-only Sandbox Mailbox for end-user app — PR-J5 §10.5.
 *
 * Differs from the admin-ui version: no mark-read, no clock controls, no
 * reset. Just a "what messages did the platform reroute back to me" view so
 * a tester running through scenarios can verify their actions emitted the
 * notifications they expected.
 */
import { useEffect, useState } from 'react'
import { Mail, Webhook, MessageSquare, X } from 'lucide-react'
import { listSandboxCaptured, getSandboxCaptured } from '@/lib/api/sandbox'
import { SandboxChannel, type SandboxChannelValue, type CapturedMessageSummaryDto, type CapturedMessageDetailDto } from '@/types/sandbox'

type TabKey = 'email' | 'webhook' | 'sms'

const TAB_CHANNEL: Record<TabKey, SandboxChannelValue> = {
  email:   SandboxChannel.Email,
  webhook: SandboxChannel.Webhook,
  sms:     SandboxChannel.Sms,
}

export function SandboxMailbox() {
  const [tab, setTab] = useState<TabKey>('email')

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-2xl font-bold text-ink">Sandbox Mailbox</h1>
        <p className="text-sm text-ink-muted">
          Outbound messages the sandbox captured (read-only view). Admins can
          mark / reset from the admin console.
        </p>
      </header>

      <div className="flex gap-1 border-b border-rule">
        <TabBtn active={tab === 'email'}   onClick={() => setTab('email')}  icon={<Mail className="h-3.5 w-3.5" />}>Email</TabBtn>
        <TabBtn active={tab === 'webhook'} onClick={() => setTab('webhook')} icon={<Webhook className="h-3.5 w-3.5" />}>Webhook</TabBtn>
        <TabBtn active={tab === 'sms'}     onClick={() => setTab('sms')}    icon={<MessageSquare className="h-3.5 w-3.5" />}>SMS</TabBtn>
      </div>

      <CapturedReadOnlyList channel={TAB_CHANNEL[tab]} />
    </div>
  )
}

function TabBtn({ active, onClick, icon, children }: { active: boolean; onClick: () => void; icon: React.ReactNode; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={[
        'inline-flex items-center gap-1.5 border-b-2 px-3 py-1.5 text-xs font-medium transition-colors',
        active ? 'border-amber-500 text-ink' : 'border-transparent text-ink-muted hover:text-ink',
      ].join(' ')}
    >
      {icon} {children}
    </button>
  )
}

function CapturedReadOnlyList({ channel }: { channel: SandboxChannelValue }) {
  const [rows, setRows] = useState<CapturedMessageSummaryDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setRows(null); setError(null); setSelectedId(null)
    listSandboxCaptured(channel)
      .then(r => { if (!cancelled) setRows(r) })
      .catch(e => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [channel])

  if (error) return <div className="rounded border border-rose-200 bg-rose-50 p-2 text-xs text-rose-800">{error}</div>
  if (!rows) return <p className="text-xs text-ink-muted">Loading…</p>
  if (rows.length === 0) return <p className="text-xs text-ink-muted">No messages captured yet for this channel.</p>

  return (
    <>
      <ul className="divide-y divide-rule overflow-hidden rounded border border-rule">
        {rows.map(r => (
          <li
            key={r.id}
            onClick={() => setSelectedId(r.id)}
            className="cursor-pointer bg-white px-3 py-2 hover:bg-slate-50"
          >
            <div className="flex items-center justify-between gap-2">
              <span className="truncate text-sm font-medium text-ink">{r.subject ?? r.eventType ?? '(no subject)'}</span>
              <span className="shrink-0 text-[10.5px] text-ink-faint">{new Date(r.capturedAt).toLocaleString()}</span>
            </div>
          </li>
        ))}
      </ul>

      {selectedId && <ReadOnlyDetail id={selectedId} onClose={() => setSelectedId(null)} />}
    </>
  )
}

function ReadOnlyDetail({ id, onClose }: { id: string; onClose: () => void }) {
  const [d, setD] = useState<CapturedMessageDetailDto | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setD(null); setErr(null)
    getSandboxCaptured(id)
      .then(detail => { if (!cancelled) setD(detail) })
      .catch(e => { if (!cancelled) setErr(e instanceof Error ? e.message : String(e)) })
    return () => { cancelled = true }
  }, [id])

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      <div className="flex max-h-[85vh] w-full max-w-2xl flex-col rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()}>
        <div className="flex items-center justify-between border-b border-rule px-5 py-3">
          <h3 className="text-base font-bold text-ink">Captured message</h3>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700">
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-auto p-5 text-sm">
          {err && <p className="text-xs text-rose-700">{err}</p>}
          {!d && !err && <p className="text-xs text-ink-muted">Loading…</p>}
          {d && (
            <div className="space-y-3">
              {d.subject && <p><span className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">Subject </span>{d.subject}</p>}
              {d.eventType && <p className="font-mono text-[11px]"><span className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">Event </span>{d.eventType}</p>}
              {d.url && <p className="font-mono text-[11px] break-all"><span className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">URL </span>{d.url}</p>}
              <p className="font-mono text-[11px]"><span className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">Recipients </span>{d.intendedRecipients.join(', ') || '—'}</p>
              {d.bodyHtml && (
                <iframe title="captured-html" srcDoc={d.bodyHtml} sandbox="" className="h-72 w-full rounded border border-rule bg-white" />
              )}
              {!d.bodyHtml && d.bodyText && <pre className="max-h-60 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px]">{d.bodyText}</pre>}
              {!d.bodyHtml && !d.bodyText && d.body && <pre className="max-h-60 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px] whitespace-pre-wrap">{d.body}</pre>}
              {d.payloadJson && <pre className="max-h-72 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px]">{tryPretty(d.payloadJson)}</pre>}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function tryPretty(s: string): string {
  try { return JSON.stringify(JSON.parse(s), null, 2) }
  catch { return s }
}
