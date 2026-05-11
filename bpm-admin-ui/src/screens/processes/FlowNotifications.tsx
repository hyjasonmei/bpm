/**
 * FlowNotifications (PR-K5 §9). v1 surface that lists captured email /
 * webhook deliveries from sandbox runs, optionally filtered by spec code
 * (joined client-side via the captured row's processInstanceId → instance).
 *
 * The proposal called for a first-class NotificationDispatchAudits table;
 * v1 ships against the existing SandboxCapturedMessages store so the
 * dashboard has data to show without a schema migration. The shipped
 * audit table is deferred to a future change that couples the notification
 * engine + audit together.
 *
 * The page is a thin reader; the heavy lifting (mark-as-read, persona
 * routing) lives in the existing Sandbox Mailbox screen.
 */

import { useCallback, useEffect, useMemo, useState } from 'react'
import { Bell, AlertTriangle, RefreshCw, Mail, Webhook, MessageCircle } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { listCaptured, getSandboxStatus } from '@/lib/api/sandbox'
import { listDefinitions, listActiveCases, listCompletedCases } from '@/lib/api/processAdmin'
import type { CapturedMessageSummaryDto } from '@/types/sandbox'
import type { FlowDefinitionDto } from '@/lib/api/processAdmin'

type ChannelFilter = '' | 'Email' | 'Webhook' | 'Sms'

export function FlowNotifications() {
  const [defs, setDefs] = useState<FlowDefinitionDto[]>([])
  const [items, setItems] = useState<CapturedMessageSummaryDto[] | null>(null)
  const [instanceToSpec, setInstanceToSpec] = useState<Map<string, string>>(new Map())
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [sandboxOn, setSandboxOn] = useState<boolean | null>(null)

  const [specCode, setSpecCode] = useState<string>('')
  const [channel, setChannel] = useState<ChannelFilter>('')

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      const status = await getSandboxStatus()
      setSandboxOn(status.enabled)
      if (!status.enabled) {
        // The captured endpoint 403s when sandbox is off; surface the
        // informational state instead of trying to load.
        setItems([])
        setError(null)
        return
      }
      const rows = await listCaptured({
        channel: channel === '' ? null : channel === 'Email' ? 1 : channel === 'Webhook' ? 2 : 3,
        limit: 200,
      })
      setItems(rows)
      setError(null)

      // Build instanceId → specCode map by querying both active and completed
      // case endpoints. v1 boundary: if we miss an instance (cancelled and
      // pruned, etc.) the row simply won't be filterable by spec.
      const instanceIds = new Set(rows.map(r => r.processInstanceId).filter((s): s is string => !!s))
      if (instanceIds.size === 0) {
        setInstanceToSpec(new Map())
        return
      }
      const map = new Map<string, string>()
      try {
        const active = await listActiveCases({ limit: 500 })
        for (const a of active) if (instanceIds.has(a.id)) map.set(a.id, a.specCode)
      } catch { /* best-effort */ }
      try {
        const completed = await listCompletedCases({ limit: 500 })
        for (const c of completed.items) if (instanceIds.has(c.id)) map.set(c.id, c.specCode)
      } catch { /* best-effort */ }
      setInstanceToSpec(map)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRefreshing(false)
    }
  }, [channel])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    void listDefinitions().then(setDefs).catch(() => setDefs([]))
  }, [])

  // Client-side spec filter — apply the join after fetch so changing the
  // dropdown doesn't trigger a refetch.
  const filtered = useMemo(() => {
    if (!items) return null
    if (!specCode) return items
    return items.filter(r => r.processInstanceId && instanceToSpec.get(r.processInstanceId) === specCode)
  }, [items, specCode, instanceToSpec])

  // Group by spec for the display.
  const grouped = useMemo(() => {
    const groups = new Map<string, CapturedMessageSummaryDto[]>()
    if (!filtered) return groups
    for (const row of filtered) {
      const sc = row.processInstanceId ? (instanceToSpec.get(row.processInstanceId) ?? '(unknown spec)') : '(no instance)'
      const list = groups.get(sc) ?? []
      list.push(row)
      groups.set(sc, list)
    }
    return groups
  }, [filtered, instanceToSpec])

  return (
    <div className="space-y-4">
      <header className="space-y-2">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-ink">Flow Notifications</h2>
          <div className="text-xs text-ink-muted">
            <Bell className="inline h-3 w-3 mr-1" />sandbox-captured deliveries
          </div>
        </div>
        <div className="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900">
          v1 view: this list shows notifications captured during sandbox runs (the
          mailbox source). A first-class production notification audit ships in a
          future change coupling the notification engine + audit table.
        </div>

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
            <span className="text-ink-muted">Channel</span>
            <select
              value={channel}
              onChange={e => setChannel(e.target.value as ChannelFilter)}
              className="rounded border border-rule px-2 py-1"
            >
              <option value="">All</option>
              <option value="Email">Email</option>
              <option value="Webhook">Webhook</option>
              <option value="Sms">SMS</option>
            </select>
          </label>

          <Button variant="outline" size="sm" onClick={() => void refresh()} disabled={refreshing}>
            <RefreshCw className={`h-3 w-3 mr-1 ${refreshing ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>
      </header>

      {sandboxOn === false && (
        <div className="rounded border border-rule bg-card p-4 text-center text-xs text-ink-muted">
          Sandbox mode is off — captured messages are not retained. Toggle sandbox
          mode on the Sandbox screen to start collecting notification deliveries.
        </div>
      )}

      {error && (
        <div className="rounded border border-red-300 bg-red-50 p-3 text-xs text-red-800 flex items-start gap-2">
          <AlertTriangle className="h-4 w-4 mt-0.5" />
          <div>
            <div className="font-semibold">Failed to load notifications</div>
            <div className="font-mono whitespace-pre-wrap">{error}</div>
          </div>
        </div>
      )}

      {filtered != null && filtered.length === 0 && sandboxOn !== false && (
        <div className="text-xs text-ink-faint">No captured notifications match the filters.</div>
      )}

      {Array.from(grouped.entries()).map(([sc, rows]) => (
        <section key={sc} className="space-y-2 rounded border border-rule bg-card p-3">
          <div className="flex items-center gap-2 text-sm font-semibold text-ink">
            <span className="font-mono">{sc}</span>
            <span className="text-xs text-ink-muted">({rows.length} delivery{rows.length === 1 ? '' : 'ies'})</span>
          </div>
          <table className="w-full text-xs">
            <thead className="text-ink-muted border-b border-rule">
              <tr className="text-left">
                <th className="py-1">Channel</th>
                <th>Subject / Event</th>
                <th>Process instance</th>
                <th>Captured</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-rule">
              {rows.map(r => (
                <tr key={r.id}>
                  <td className="py-1"><ChannelIcon channel={r.channel} /></td>
                  <td>{r.subject ?? r.eventType ?? <span className="text-ink-faint">(no subject)</span>}</td>
                  <td className="font-mono text-[11px]">{r.processInstanceId?.slice(0, 8) ?? '—'}</td>
                  <td title={r.capturedAt}>{fmtTime(r.capturedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
    </div>
  )
}

function ChannelIcon({ channel }: { channel: number }) {
  if (channel === 1) return <span className="inline-flex items-center gap-1"><Mail className="h-3 w-3" />Email</span>
  if (channel === 2) return <span className="inline-flex items-center gap-1"><Webhook className="h-3 w-3" />Webhook</span>
  if (channel === 3) return <span className="inline-flex items-center gap-1"><MessageCircle className="h-3 w-3" />SMS</span>
  return <span>{String(channel)}</span>
}

function fmtTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString(undefined, { hour12: false })
  } catch { return iso }
}
