/**
 * CompletedCases (PR-K5 §7). Admin monitor for terminal (Completed /
 * Cancelled) ProcessInstance rows. Filter by spec, terminal-after date,
 * and status (Completed only / Cancelled only / both). Cursor pagination
 * via "Load more" — backend returns up to 100 rows per call by default.
 *
 * Row click reuses <LiveCaseDetail> for the read-only inspection flow;
 * the drawer's intervention buttons no-op on terminal instances (the
 * drawer only renders Terminate when status is Running/Errored).
 */

import { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, RefreshCw, CheckCircle, FileDown } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  listCompletedCases, listDefinitions,
  type CompletedCaseDto, type FlowDefinitionDto,
} from '@/lib/api/processAdmin'
import { LiveCaseDetail } from './LiveCaseDetail'

type StatusFilter = '' | 'completed' | 'cancelled'

export function CompletedCases() {
  const [defs, setDefs] = useState<FlowDefinitionDto[]>([])
  const [items, setItems] = useState<CompletedCaseDto[] | null>(null)
  const [cursor, setCursor] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [loadingMore, setLoadingMore] = useState(false)

  const [specCode, setSpecCode] = useState<string>('')        // '' = all
  const [completedAfter, setCompletedAfter] = useState<string>('')
  const [status, setStatus] = useState<StatusFilter>('')

  const [openInstanceId, setOpenInstanceId] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      const page = await listCompletedCases({
        specCode: specCode || null,
        completedAfter: completedAfter ? new Date(completedAfter).toISOString() : null,
        status: status === '' ? null : status,
        limit: 50,
      })
      setItems(page.items)
      setCursor(page.nextCursor)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRefreshing(false)
    }
  }, [specCode, completedAfter, status])

  const loadMore = useCallback(async () => {
    if (!cursor) return
    setLoadingMore(true)
    try {
      const page = await listCompletedCases({
        specCode: specCode || null,
        completedAfter: completedAfter ? new Date(completedAfter).toISOString() : null,
        status: status === '' ? null : status,
        limit: 50,
        cursor,
      })
      setItems(prev => (prev ?? []).concat(page.items))
      setCursor(page.nextCursor)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoadingMore(false)
    }
  }, [cursor, specCode, completedAfter, status])

  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    void listDefinitions().then(setDefs).catch(() => setDefs([]))
  }, [])

  return (
    <div className="space-y-4">
      <header className="space-y-2">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-ink">Completed Cases</h2>
          <div className="text-xs text-ink-muted">
            <CheckCircle className="inline h-3 w-3 mr-1" />terminal instances + cycle time
          </div>
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
            <span className="text-ink-muted">Terminal after</span>
            <input
              type="date"
              value={completedAfter}
              onChange={e => setCompletedAfter(e.target.value)}
              className="rounded border border-rule px-2 py-1"
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-ink-muted">Status</span>
            <select
              value={status}
              onChange={e => setStatus(e.target.value as StatusFilter)}
              className="rounded border border-rule px-2 py-1"
            >
              <option value="">Both</option>
              <option value="completed">Completed only</option>
              <option value="cancelled">Cancelled only</option>
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
            <div className="font-semibold">Failed to load completed cases</div>
            <div className="font-mono whitespace-pre-wrap">{error}</div>
          </div>
        </div>
      )}

      <table className="w-full text-xs">
        <thead className="text-ink-muted border-b border-rule">
          <tr className="text-left">
            <th className="py-2">Spec</th>
            <th>Initiator</th>
            <th>Started</th>
            <th>Terminated</th>
            <th>Cycle time</th>
            <th>Status</th>
            <th>PDF</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-rule">
          {items == null ? (
            <tr><td colSpan={7} className="py-6 text-center text-ink-faint">Loading…</td></tr>
          ) : items.length === 0 ? (
            <tr><td colSpan={7} className="py-6 text-center text-ink-faint">No completed cases match the filters.</td></tr>
          ) : items.map(c => {
            const terminal = c.completedAt ?? c.cancelledAt
            return (
              <tr
                key={c.id}
                onClick={() => setOpenInstanceId(c.id)}
                className="cursor-pointer hover:bg-slate-50"
              >
                <td className="py-2 font-mono">{c.specCode} <span className="text-ink-faint">v{c.specVersion}</span></td>
                <td>{c.initiatorEmail ?? <code>{shortId(c.initiatorUserId)}</code>}</td>
                <td title={c.startedAt}>{fmtDate(c.startedAt)}</td>
                <td title={terminal ?? ''}>{terminal ? fmtDate(terminal) : '—'}</td>
                <td>{formatCycleTime(c.cycleTime)}</td>
                <td>
                  <span className={`inline-block rounded px-2 py-0.5 ${
                    c.status === 'Completed'
                      ? 'bg-emerald-100 text-emerald-800'
                      : 'bg-slate-200 text-slate-700'
                  }`}>{c.status}</span>
                </td>
                <td onClick={e => e.stopPropagation()}>
                  <PdfExportButton instanceId={c.id} />
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>

      {cursor && (
        <div className="flex justify-center pt-2">
          <Button variant="outline" size="sm" onClick={() => void loadMore()} disabled={loadingMore}>
            {loadingMore ? 'Loading…' : 'Load more'}
          </Button>
        </div>
      )}

      {openInstanceId && (
        <LiveCaseDetail instanceId={openInstanceId} onClose={() => setOpenInstanceId(null)} />
      )}
    </div>
  )
}

/**
 * §7.3: PDF export ships in `add-pdf-export`. Scaffolded button surfaces
 * the future affordance without overpromising.
 */
function PdfExportButton({ instanceId }: { instanceId: string }) {
  return (
    <button
      title={`PDF export ships in add-pdf-export proposal (instance ${instanceId.slice(0, 8)})`}
      disabled
      className="inline-flex items-center gap-1 rounded border border-rule px-2 py-0.5 text-[11px] text-ink-faint cursor-not-allowed"
    >
      <FileDown className="h-3 w-3" />PDF
    </button>
  )
}

function shortId(s: string | null): string {
  if (!s) return '—'
  return s.slice(0, 8)
}

function fmtDate(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString(undefined, { hour12: false })
  } catch { return iso }
}

/**
 * Backend serializes TimeSpan as either the .NET form `0.02:34:21.5`
 * (days.hh:mm:ss.fff) or `02:34:21.5` (hh:mm:ss.fff). We render either as
 * the friendly form "2d 5h" / "2h 14m" / "5m" / "<1m".
 */
function formatCycleTime(raw: string | null): string {
  if (!raw) return '—'
  // Normalize to seconds using a permissive parser.
  const m = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)/.exec(raw)
  if (!m) return raw
  const days = m[1] ? parseInt(m[1], 10) : 0
  const hours = parseInt(m[2], 10)
  const minutes = parseInt(m[3], 10)
  const totalHours = days * 24 + hours
  if (totalHours >= 24) return `${Math.floor(totalHours / 24)}d ${totalHours % 24}h`
  if (totalHours >= 1) return `${totalHours}h ${minutes}m`
  if (minutes >= 1) return `${minutes}m`
  return '<1m'
}
