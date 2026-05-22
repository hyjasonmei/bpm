/**
 * FlowNotifications — Phase 2.1b rewrite.
 *
 * Reads from the new `NotificationDispatchAudits` table (Phase 2.1)
 * via GET /api/admin/process-admin/notification-audits. This is the
 * canonical production audit: every notification fired by the runtime
 * lands a row regardless of sandbox state.
 *
 * The page is a thin reader; production-side delivery (real email /
 * Teams / webhook) still lives in the future add-notification-engine
 * change. Audit row shape is forward-compatible — when real delivery
 * lands, `status` flips from `dispatched` (log-only today) to
 * `dispatched`/`failed` with richer recipient info.
 */

import { useCallback, useEffect, useState } from 'react'
import { Bell, AlertTriangle, RefreshCw, ShieldAlert, CheckCircle2, Inbox } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  listDefinitions,
  listNotificationAudits,
  type FlowDefinitionDto,
  type NotificationAuditDto,
  type NotificationAuditStatus,
} from '@/lib/api/processAdmin'

type StatusFilter = '' | NotificationAuditStatus

export function FlowNotifications() {
  const [defs, setDefs] = useState<FlowDefinitionDto[]>([])
  const [items, setItems] = useState<NotificationAuditDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)

  const [specCode, setSpecCode] = useState<string>('')
  const [status, setStatus] = useState<StatusFilter>('')

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      const rows = await listNotificationAudits({
        specCode: specCode || undefined,
        status: status || undefined,
        limit: 200,
      })
      setItems(rows)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRefreshing(false)
    }
  }, [specCode, status])

  useEffect(() => { void refresh() }, [refresh])

  useEffect(() => {
    void listDefinitions().then(setDefs).catch(() => setDefs([]))
  }, [])

  // Group rows by spec for the display.
  const grouped = new Map<string, NotificationAuditDto[]>()
  for (const row of items ?? []) {
    const list = grouped.get(row.specCode) ?? []
    list.push(row)
    grouped.set(row.specCode, list)
  }

  return (
    <div className="space-y-4">
      <header className="space-y-2">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-ink">Flow Notifications</h2>
          <div className="text-xs text-ink-muted">
            <Bell className="inline h-3 w-3 mr-1" />
            append-only dispatch audit
          </div>
        </div>
        <p className="text-xs text-ink-muted">
          Every notification fired by the runtime — sandbox-captured or production-dispatched — lands one row in <code className="font-mono">NotificationDispatchAudits</code>. Use this view to triage what went out, where it went, and which dispatches failed.
        </p>

        <div className="flex flex-wrap items-end gap-3 text-xs">
          <label className="flex flex-col gap-1">
            <span className="text-ink-muted">Spec</span>
            <select
              value={specCode}
              onChange={e => setSpecCode(e.target.value)}
              className="rounded border border-rule px-2 py-1 min-w-[180px]"
            >
              <option value="">All specs</option>
              {defs.map(d => (
                <option key={`${d.source}-${d.flowCode}`} value={d.flowCode}>{d.flowCode}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-ink-muted">Status</span>
            <select
              value={status}
              onChange={e => setStatus(e.target.value as StatusFilter)}
              className="rounded border border-rule px-2 py-1"
            >
              <option value="">All</option>
              <option value="captured">captured (sandbox)</option>
              <option value="dispatched">dispatched</option>
              <option value="failed">failed</option>
            </select>
          </label>

          <Button variant="outline" size="sm" onClick={() => void refresh()} disabled={refreshing}>
            <RefreshCw className={`h-3 w-3 mr-1 ${refreshing ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>
      </header>

      {error && (
        <div className="rounded border border-red-300 bg-red-50 p-3 text-xs text-red-800 flex items-start gap-2">
          <AlertTriangle className="h-4 w-4 mt-0.5" />
          <div>
            <div className="font-semibold">Failed to load notification audits</div>
            <div className="font-mono whitespace-pre-wrap">{error}</div>
          </div>
        </div>
      )}

      {items != null && items.length === 0 && !error && (
        <div className="rounded border border-dashed border-rule bg-card p-6 text-center text-xs text-ink-muted">
          No dispatches recorded yet. Submit a flow with notifications to populate the audit.
        </div>
      )}

      {Array.from(grouped.entries()).map(([sc, rows]) => (
        <section key={sc} className="space-y-2 rounded border border-rule bg-card p-3">
          <div className="flex items-center gap-2 text-sm font-semibold text-ink">
            <span className="font-mono">{sc}</span>
            <span className="text-xs text-ink-muted">({rows.length} dispatch{rows.length === 1 ? '' : 'es'})</span>
          </div>
          <table className="w-full text-xs">
            <thead className="text-ink-muted border-b border-rule">
              <tr className="text-left">
                <th className="py-1 w-24">Status</th>
                <th className="w-32">Trigger</th>
                <th>Subject / Body</th>
                <th className="w-40">Recipient</th>
                <th className="w-32">Instance</th>
                <th className="w-40">Dispatched</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-rule">
              {rows.map(r => (
                <tr key={r.id} className="align-top">
                  <td className="py-1.5"><StatusBadge status={r.status} /></td>
                  <td className="font-mono text-[11px]">{r.trigger}</td>
                  <td className="min-w-0">
                    <div className="truncate">{r.subject ?? <span className="text-ink-faint">(no subject)</span>}</div>
                    {r.body && <div className="truncate text-[10.5px] text-ink-muted">{r.body}</div>}
                    {r.error && <div className="truncate text-[10.5px] text-red-700">⚠ {r.error}</div>}
                  </td>
                  <td className="truncate font-mono text-[10.5px]">{r.recipient ?? '—'}</td>
                  <td className="font-mono text-[10.5px]">{r.instanceId.slice(0, 8)}</td>
                  <td title={r.dispatchedAt} className="text-[11px]">{fmtTime(r.dispatchedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
    </div>
  )
}

function StatusBadge({ status }: { status: NotificationAuditStatus }) {
  if (status === 'captured') {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide text-amber-800">
        <Inbox className="h-2.5 w-2.5" /> captured
      </span>
    )
  }
  if (status === 'dispatched') {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide text-emerald-800">
        <CheckCircle2 className="h-2.5 w-2.5" /> dispatched
      </span>
    )
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-red-100 px-2 py-0.5 font-mono text-[10px] uppercase tracking-wide text-red-800">
      <ShieldAlert className="h-2.5 w-2.5" /> failed
    </span>
  )
}

function fmtTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString(undefined, { hour12: false })
  } catch { return iso }
}
