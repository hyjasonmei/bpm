/**
 * Reports (PR-K5 §8). Per-spec aggregate dashboard:
 *   - Stat cards: TotalStarted / Completed / Cancelled / Running / BreachRate.
 *   - Cycle-time histogram: inline SVG bar chart, no chart lib.
 *   - Bottlenecks table (top 20 by AvgWaitTime).
 *   - Per-assignee load table (top 50 by OpenTasks).
 *
 * CSV export builds a flat CSV per section in the browser; PDF export is
 * scaffolded with a "Coming Soon" hint pointing at `add-pdf-export`.
 *
 * The backend caches per-spec/period for 5 min (TTL-only invalidation); we
 * don't poll — the dashboard is a one-shot view.
 */

import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertTriangle, RefreshCw, BarChart, FileDown, Download } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  getPerSpecReport, listDefinitions,
  type PerSpecReportDto, type FlowDefinitionDto, type ReportPeriodToken,
} from '@/lib/api/processAdmin'

const PERIOD_OPTIONS: Array<{ label: string; value: ReportPeriodToken }> = [
  { label: 'Last 7 days',  value: '7d'  },
  { label: 'Last 30 days', value: '30d' },
  { label: 'Last 90 days', value: '90d' },
  { label: 'All time',     value: 'all' },
]

export function Reports() {
  const [defs, setDefs] = useState<FlowDefinitionDto[]>([])
  const [specCode, setSpecCode] = useState<string>('')
  const [period, setPeriod] = useState<ReportPeriodToken>('30d')
  const [report, setReport] = useState<PerSpecReportDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const refresh = useCallback(async () => {
    if (!specCode) {
      setReport(null)
      return
    }
    setLoading(true)
    try {
      const r = await getPerSpecReport(specCode, period)
      setReport(r)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [specCode, period])

  useEffect(() => {
    void listDefinitions().then(rows => {
      setDefs(rows)
      // Auto-pick the first spec so the page isn't blank on first load.
      if (rows.length > 0 && !specCode) setSpecCode(rows[0].flowCode)
    }).catch(() => setDefs([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  return (
    <div className="space-y-4">
      <header className="space-y-2">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-ink">Reports</h2>
          <div className="text-xs text-ink-muted">
            <BarChart className="inline h-3 w-3 mr-1" />cached server-side for 5 min
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
              <option value="">— pick a spec —</option>
              {defs.map(d => (
                <option key={`${d.source}-${d.flowCode}`} value={d.flowCode}>{d.flowCode}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-ink-muted">Period</span>
            <select
              value={period}
              onChange={e => setPeriod(e.target.value as ReportPeriodToken)}
              className="rounded border border-rule px-2 py-1"
            >
              {PERIOD_OPTIONS.map(o => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </label>

          <Button variant="outline" size="sm" onClick={() => void refresh()} disabled={loading || !specCode}>
            <RefreshCw className={`h-3 w-3 mr-1 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </Button>

          {report && (
            <>
              <Button variant="outline" size="sm" onClick={() => exportCsv(report)}>
                <Download className="h-3 w-3 mr-1" />Export CSV
              </Button>
              <button
                disabled
                title="PDF export ships in add-pdf-export proposal"
                className="inline-flex items-center gap-1 rounded border border-rule px-2 py-1 text-xs text-ink-faint cursor-not-allowed"
              >
                <FileDown className="h-3 w-3" />Export PDF
              </button>
            </>
          )}
        </div>
      </header>

      {error && (
        <div className="rounded border border-red-300 bg-red-50 p-3 text-xs text-red-800 flex items-start gap-2">
          <AlertTriangle className="h-4 w-4 mt-0.5" />
          <div>
            <div className="font-semibold">Failed to load report</div>
            <div className="font-mono whitespace-pre-wrap">{error}</div>
          </div>
        </div>
      )}

      {!specCode && (
        <div className="text-xs text-ink-faint">Pick a spec above to load aggregate stats.</div>
      )}

      {report && (
        <>
          <StatCards report={report} />
          <CycleTimeSection report={report} />
          <BottlenecksTable report={report} />
          <AssigneeLoadTable report={report} />
        </>
      )}
    </div>
  )
}

function StatCards({ report }: { report: PerSpecReportDto }) {
  const cards = [
    { label: 'Started',   value: String(report.totalStarted)   },
    { label: 'Completed', value: String(report.totalCompleted) },
    { label: 'Cancelled', value: String(report.totalCancelled) },
    { label: 'Running',   value: String(report.totalRunning)   },
    { label: 'Breach rate', value: `${(report.breachRate * 100).toFixed(1)}%`, accent: report.breachRate > 0.1 },
  ]
  return (
    <div className="grid grid-cols-5 gap-3">
      {cards.map(c => (
        <div key={c.label} className="rounded border border-rule bg-card p-3">
          <div className="text-xs text-ink-muted">{c.label}</div>
          <div className={`text-lg font-semibold ${c.accent ? 'text-red-700' : 'text-ink'}`}>{c.value}</div>
        </div>
      ))}
    </div>
  )
}

function CycleTimeSection({ report }: { report: PerSpecReportDto }) {
  return (
    <section className="space-y-2 rounded border border-rule bg-card p-4">
      <div className="flex items-end justify-between">
        <div>
          <div className="text-sm font-semibold text-ink">Cycle time</div>
          <div className="text-xs text-ink-muted">
            avg {report.avgCycleTimeHours.toFixed(2)}h ·
            P50 {report.p50CycleTimeHours.toFixed(2)}h ·
            P90 {report.p90CycleTimeHours.toFixed(2)}h
          </div>
        </div>
      </div>
      <Histogram buckets={report.cycleTimeHistogram} />
    </section>
  )
}

function Histogram({ buckets }: { buckets: PerSpecReportDto['cycleTimeHistogram'] }) {
  const max = useMemo(() => Math.max(1, ...buckets.map(b => b.count)), [buckets])
  // Inline SVG bar chart: each bar fills proportionally to max. Width is
  // fixed-ish (responsive via parent's width), height is constant 110px.
  const barWidth = 70
  const gap = 16
  const totalWidth = buckets.length * (barWidth + gap)
  const chartHeight = 110
  return (
    <div className="overflow-x-auto pt-1">
      <svg width={totalWidth} height={chartHeight + 30} className="text-ink">
        {buckets.map((b, i) => {
          const h = max === 0 ? 0 : Math.round((b.count / max) * chartHeight)
          const x = i * (barWidth + gap)
          const y = chartHeight - h
          return (
            <g key={b.range}>
              <rect x={x} y={y} width={barWidth} height={h} className="fill-amber-400" />
              <text x={x + barWidth / 2} y={chartHeight - h - 4} fontSize="10" textAnchor="middle" className="fill-ink-muted">
                {b.count}
              </text>
              <text x={x + barWidth / 2} y={chartHeight + 16} fontSize="10" textAnchor="middle" className="fill-ink-muted">
                {b.range}
              </text>
            </g>
          )
        })}
      </svg>
    </div>
  )
}

function BottlenecksTable({ report }: { report: PerSpecReportDto }) {
  return (
    <section className="space-y-2 rounded border border-rule bg-card p-4">
      <div className="text-sm font-semibold text-ink">Bottlenecks (top 20 by avg wait)</div>
      {report.bottlenecks.length === 0 ? (
        <div className="text-xs text-ink-faint">No completed tasks in this window.</div>
      ) : (
        <table className="w-full text-xs">
          <thead className="text-ink-muted border-b border-rule">
            <tr className="text-left">
              <th className="py-1">Node</th>
              <th>Avg wait</th>
              <th>Completed count</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-rule">
            {report.bottlenecks.map(b => (
              <tr key={b.nodeId}>
                <td className="py-1 font-mono">{b.nodeId}</td>
                <td>{b.avgWaitTimeHours.toFixed(2)}h</td>
                <td>{b.completedCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

function AssigneeLoadTable({ report }: { report: PerSpecReportDto }) {
  return (
    <section className="space-y-2 rounded border border-rule bg-card p-4">
      <div className="text-sm font-semibold text-ink">Per-assignee load (top 50 by open tasks)</div>
      {report.assigneeLoads.length === 0 ? (
        <div className="text-xs text-ink-faint">No tasks assigned.</div>
      ) : (
        <table className="w-full text-xs">
          <thead className="text-ink-muted border-b border-rule">
            <tr className="text-left">
              <th className="py-1">Assignee</th>
              <th>Open</th>
              <th>Completed</th>
              <th>Avg task time</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-rule">
            {report.assigneeLoads.map(a => (
              <tr key={a.userId}>
                <td className="py-1">{a.email ?? <code>{a.userId.slice(0, 8)}</code>}</td>
                <td>{a.openTasks}</td>
                <td>{a.completedTasks}</td>
                <td>{a.avgTaskTimeHours.toFixed(2)}h</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

/**
 * §8.6 — CSV export. We flatten the report into three sub-sections joined by
 * blank-line separators so a spreadsheet imports cleanly. No external lib —
 * CSV is trivial as long as we quote everything.
 */
function exportCsv(report: PerSpecReportDto) {
  const lines: string[] = []
  lines.push('Section,Field,Value')
  lines.push(`headline,specCode,${q(report.specCode)}`)
  lines.push(`headline,period,${q(String(report.period))}`)
  lines.push(`headline,totalStarted,${report.totalStarted}`)
  lines.push(`headline,totalCompleted,${report.totalCompleted}`)
  lines.push(`headline,totalCancelled,${report.totalCancelled}`)
  lines.push(`headline,totalRunning,${report.totalRunning}`)
  lines.push(`headline,avgCycleTimeHours,${report.avgCycleTimeHours.toFixed(4)}`)
  lines.push(`headline,p50CycleTimeHours,${report.p50CycleTimeHours.toFixed(4)}`)
  lines.push(`headline,p90CycleTimeHours,${report.p90CycleTimeHours.toFixed(4)}`)
  lines.push(`headline,breachCount,${report.breachCount}`)
  lines.push(`headline,breachRate,${report.breachRate.toFixed(4)}`)
  lines.push('')
  lines.push('Section,Range,Count')
  for (const b of report.cycleTimeHistogram) {
    lines.push(`histogram,${q(b.range)},${b.count}`)
  }
  lines.push('')
  lines.push('Section,NodeId,AvgWaitTimeHours,CompletedCount')
  for (const b of report.bottlenecks) {
    lines.push(`bottleneck,${q(b.nodeId)},${b.avgWaitTimeHours.toFixed(4)},${b.completedCount}`)
  }
  lines.push('')
  lines.push('Section,UserId,Email,OpenTasks,CompletedTasks,AvgTaskTimeHours')
  for (const a of report.assigneeLoads) {
    lines.push(`assignee,${q(a.userId)},${q(a.email ?? '')},${a.openTasks},${a.completedTasks},${a.avgTaskTimeHours.toFixed(4)}`)
  }
  const csv = lines.join('\n')
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `report_${report.specCode}_${report.period}.csv`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

function q(v: string): string {
  if (v.includes(',') || v.includes('"') || v.includes('\n')) {
    return '"' + v.replace(/"/g, '""') + '"'
  }
  return v
}
