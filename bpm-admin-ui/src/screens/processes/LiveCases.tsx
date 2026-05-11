/**
 * LiveCases (PR-K4 §5.1) — admin monitor for Running / Errored process
 * instances. Filters: spec dropdown (from `/api/admin/process-admin/definitions`),
 * age window (1d / 7d / 30d / all), and breach-only checkbox.
 *
 * Polls /cases/active every 30s, paused when the tab is hidden (see
 * `usePolling`). Row click opens <LiveCaseDetail> in a drawer; the drawer
 * is responsible for its own 15s poll.
 */

import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, RefreshCw, Activity, Clock } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  listActiveCases, listDefinitions,
  type ActiveCaseDto, type FlowDefinitionDto,
} from '@/lib/api/processAdmin'
import { LiveCaseDetail } from './LiveCaseDetail'
import { usePolling } from '@/hooks/usePolling'

const AGE_OPTIONS: Array<{ label: string; value: number | null }> = [
  { label: '24 hours',  value: 1 },
  { label: '7 days',    value: 7 },
  { label: '30 days',   value: 30 },
  { label: 'All',       value: null },
]

export function LiveCases() {
  const [defs, setDefs] = useState<FlowDefinitionDto[]>([])
  const [cases, setCases] = useState<ActiveCaseDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)

  const [specCode, setSpecCode] = useState<string>('') // '' = all
  const [maxAgeDays, setMaxAgeDays] = useState<number | null>(7)
  const [breachOnly, setBreachOnly] = useState(false)

  const [openInstanceId, setOpenInstanceId] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      const next = await listActiveCases({
        specCode: specCode || null,
        maxAgeDays: maxAgeDays,
        breachOnly,
        limit: 200,
      })
      setCases(next)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setRefreshing(false)
    }
  }, [specCode, maxAgeDays, breachOnly])

  // 30s poll, paused when tab hidden.
  usePolling(refresh, 30_000)

  // One-shot definitions fetch for the spec dropdown.
  useEffect(() => {
    void listDefinitions().then(setDefs).catch(() => setDefs([]))
  }, [])

  const counts = useMemo(() => {
    const total = cases?.length ?? 0
    const breaches = cases?.filter(c => c.hasBreach).length ?? 0
    const errored = cases?.filter(c => c.status === 'Errored').length ?? 0
    return { total, breaches, errored }
  }, [cases])

  return (
    <div className="space-y-4">
      <header className="space-y-2">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-ink">Live Cases</h2>
          <div className="text-xs text-ink-muted">
            <Activity className="inline h-3 w-3 mr-1" />polling every 30s, paused when tab hidden
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
            <span className="text-ink-muted">Age</span>
            <select
              value={maxAgeDays == null ? 'all' : String(maxAgeDays)}
              onChange={e => setMaxAgeDays(e.target.value === 'all' ? null : Number(e.target.value))}
              className="rounded border border-rule px-2 py-1"
            >
              {AGE_OPTIONS.map(o => (
                <option key={o.label} value={o.value == null ? 'all' : String(o.value)}>{o.label}</option>
              ))}
            </select>
          </label>

          <label className="flex items-center gap-1">
            <input type="checkbox" checked={breachOnly} onChange={e => setBreachOnly(e.target.checked)} />
            <span>Breach only</span>
          </label>

          <Button variant="outline" size="sm" onClick={() => void refresh()} disabled={refreshing}>
            <RefreshCw className={`h-3 w-3 mr-1 ${refreshing ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>

        <div className="flex gap-3 text-xs text-ink-muted">
          <Counter label="Active" value={counts.total} />
          <Counter label="Breaches" value={counts.breaches} className={counts.breaches > 0 ? 'text-red-600' : ''} />
          <Counter label="Errored" value={counts.errored} className={counts.errored > 0 ? 'text-amber-700' : ''} />
        </div>
      </header>

      {error && (
        <div className="rounded border border-red-300 bg-red-50 p-3 text-xs text-red-800 flex items-start gap-2">
          <AlertTriangle className="h-4 w-4 mt-0.5" />
          <div>
            <div className="font-semibold">Failed to load active cases</div>
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
            <th>Last activity</th>
            <th className="text-center">Open</th>
            <th>Current assignee</th>
            <th>Status</th>
            <th></th>
          </tr>
        </thead>
        <tbody className="divide-y divide-rule">
          {cases == null ? (
            <tr><td colSpan={8} className="py-6 text-center text-ink-faint">Loading…</td></tr>
          ) : cases.length === 0 ? (
            <tr><td colSpan={8} className="py-6 text-center text-ink-faint">No active cases match the filters.</td></tr>
          ) : cases.map(c => (
            <tr
              key={c.id}
              onClick={() => setOpenInstanceId(c.id)}
              className="cursor-pointer hover:bg-slate-50"
            >
              <td className="py-2 font-mono">{c.specCode} <span className="text-ink-faint">v{c.specVersion}</span></td>
              <td>{c.initiatorEmail ?? <code>{shortId(c.initiatorUserId)}</code>}</td>
              <td title={c.startedAt}>{rel(c.startedAt)}</td>
              <td title={c.lastActivityAt}>{rel(c.lastActivityAt)}</td>
              <td className="text-center">{c.openTaskCount}</td>
              <td>{c.currentAssigneeEmail ?? (c.openTaskCount > 1 ? <span className="text-ink-faint">({c.openTaskCount} pending)</span> : '—')}</td>
              <td>
                <span className={`inline-block rounded px-2 py-0.5 ${
                  c.status === 'Errored'
                    ? 'bg-red-100 text-red-800'
                    : 'bg-amber-100 text-amber-900'
                }`}>{c.status}</span>
              </td>
              <td>
                {c.hasBreach && (
                  <span className="inline-flex items-center gap-1 rounded bg-red-100 text-red-800 px-2 py-0.5">
                    <Clock className="h-3 w-3" />breach
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {openInstanceId && (
        <LiveCaseDetail instanceId={openInstanceId} onClose={() => setOpenInstanceId(null)} />
      )}
    </div>
  )
}

function Counter({ label, value, className = '' }: { label: string; value: number; className?: string }) {
  return (
    <span className={`inline-flex items-center gap-1 ${className}`}>
      <span>{label}:</span><span className="font-semibold">{value}</span>
    </span>
  )
}

function shortId(s: string | null): string {
  if (!s) return '—'
  return s.slice(0, 8)
}

function rel(iso: string): string {
  try {
    const d = Date.parse(iso)
    if (Number.isNaN(d)) return iso
    const deltaSec = (Date.now() - d) / 1000
    if (deltaSec < 60) return 'just now'
    if (deltaSec < 3600) return `${Math.floor(deltaSec / 60)}m ago`
    if (deltaSec < 86400) return `${Math.floor(deltaSec / 3600)}h ago`
    return `${Math.floor(deltaSec / 86400)}d ago`
  } catch { return iso }
}
