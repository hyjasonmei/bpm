import { useEffect, useMemo, useState } from 'react'
import { TrendingUp, TrendingDown, Minus, FileText, CheckCircle2, Clock, Activity, Loader2, RefreshCw } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { TypeChip } from '@/components/ui/badge'
import { getReportSummary, type ReportSummary } from '@/flowcook/api/reports'

// Cross-flow outcome buckets the backend collapses every per-flow status into.
const BUCKET_COLOR: Record<string, string> = {
  'Completed':   '#22c55e',
  'In progress': '#f59e0b',
  'Cancelled':   '#94a3b8',
  'Rejected':    '#ef4444',
}

export function ReportsPage() {
  const [data, setData] = useState<ReportSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = async () => {
    setLoading(true)
    setError(null)
    try {
      setData(await getReportSummary())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load report')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  const monthDelta = useMemo(() => {
    const m = data?.monthly ?? []
    const cur = m[m.length - 1]?.count ?? 0
    const prev = m[m.length - 2]?.count ?? 0
    return prev === 0 ? 0 : Math.round(((cur - prev) / prev) * 100)
  }, [data])

  if (loading && !data) {
    return <div className="flex items-center gap-2 px-1 py-10 text-sm text-ink-muted"><Loader2 className="h-4 w-4 animate-spin" /> 載入報表…</div>
  }
  if (error && !data) {
    return (
      <div className="space-y-3 px-1 py-8">
        <p className="text-sm text-danger">報表載入失敗：{error}</p>
        <Button variant="outline" size="sm" onClick={() => void load()}><RefreshCw className="h-3.5 w-3.5" /> 重試</Button>
      </div>
    )
  }
  if (!data) return null

  const maxByFlow = Math.max(...data.byFlow.map(b => b.count), 1)
  const approvalPct = Math.round(data.approvalRate * 100)

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Reports</h1>
          <p className="text-sm text-ink-muted">
            全流程案件統計 — {data.totalCases} 件實際案件（即時）
          </p>
        </div>
        <div className="flex items-center gap-3">
          <span className="text-[10.5px] uppercase tracking-wider text-ink-faint">last 6 months · all flows · all users</span>
          <Button variant="outline" size="xs" onClick={() => void load()} disabled={loading}>
            {loading ? <Loader2 className="h-3 w-3 animate-spin" /> : <RefreshCw className="h-3 w-3" />} Refresh
          </Button>
        </div>
      </div>

      {/* KPI strip — headline metrics; Completed / In progress live in the
          status donut below so the first row stays uncluttered. */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi icon={<FileText className="h-4 w-4" />} label="Total cases" value={data.totalCases.toString()} accent="bg-blue-50 text-blue-700" sub={<>累計件數</>} />
        <Kpi icon={<Activity className="h-4 w-4" />} label="This month" value={data.thisMonth.toString()} accent="bg-amber-50 text-amber-700" sub={<DeltaTag delta={monthDelta} />} />
        <Kpi icon={<CheckCircle2 className="h-4 w-4" />} label="Approval rate" value={`${approvalPct}%`} accent="bg-green-50 text-green-700" sub={<>結案案件通過比例</>} />
        <Kpi icon={<Clock className="h-4 w-4" />} label="Avg cycle" value={data.avgCycleDays == null ? '—' : `${data.avgCycleDays}d`} accent="bg-violet-50 text-violet-700" sub={<>送出 → 結案平均天數</>} />
      </div>

      {/* Donut + flow bars */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-5">
        <div className="lg:col-span-2">
          <SectionCard>
            <SectionTitle>Status Breakdown / 狀態分布</SectionTitle>
            <div className="p-5">
              {data.byStatus.length === 0 ? (
                <p className="py-8 text-center text-sm text-ink-faint">尚無案件</p>
              ) : (
                <DonutChart
                  data={data.byStatus.map(s => ({ key: s.bucket, label: s.bucket, value: s.count, color: BUCKET_COLOR[s.bucket] ?? '#94a3b8' }))}
                  centerLabel="Total"
                  centerValue={data.totalCases.toString()}
                />
              )}
            </div>
          </SectionCard>
        </div>

        <div className="lg:col-span-3">
          <SectionCard>
            <SectionTitle>Counts by Flow / 各流程數量</SectionTitle>
            <div className="space-y-2.5 p-5">
              {data.byFlow.length === 0 ? (
                <p className="py-8 text-center text-sm text-ink-faint">尚無案件</p>
              ) : data.byFlow.map(({ flowCode, count }) => (
                <div key={flowCode} className="grid grid-cols-[150px_1fr_50px] items-center gap-3">
                  <div className="flex items-center gap-2 min-w-0"><TypeChip type={flowCode} /></div>
                  <div className="h-7 overflow-hidden rounded bg-slate-100">
                    <div className="h-full rounded bg-gradient-to-r from-blue-500 to-blue-400 transition-all" style={{ width: `${(count / maxByFlow) * 100}%` }} />
                  </div>
                  <div className="text-right font-mono text-sm font-semibold tabular text-ink">{count}</div>
                </div>
              ))}
            </div>
          </SectionCard>
        </div>
      </div>

      {/* Monthly trend — area+line chart */}
      <SectionCard>
        <SectionTitle>Monthly Volume / 月份送件量 (last 6 months)</SectionTitle>
        <div className="p-5">
          <AreaLineChart points={data.monthly.map(m => ({ x: m.month, y: m.count }))} />
        </div>
      </SectionCard>
    </div>
  )
}

/* ─── KPI card ─────────────────────────────────────────────── */

function Kpi({
  icon, label, value, accent, sub,
}: { icon: React.ReactNode; label: string; value: string; accent: string; sub: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 rounded-lg border border-rule bg-white p-4 shadow-sm">
      <div className={cn('flex h-10 w-10 items-center justify-center rounded-md', accent)}>
        {icon}
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">{label}</div>
        <div className="font-mono text-2xl font-bold leading-tight tabular text-ink">{value}</div>
        <div className="text-[11px] text-ink-muted">{sub}</div>
      </div>
    </div>
  )
}

function DeltaTag({ delta }: { delta: number }) {
  if (delta === 0) return <span className="inline-flex items-center gap-0.5 text-ink-faint"><Minus className="h-3 w-3" />持平</span>
  if (delta > 0) return <span className="inline-flex items-center gap-0.5 text-green-600"><TrendingUp className="h-3 w-3" />+{delta}% vs 上月</span>
  return <span className="inline-flex items-center gap-0.5 text-rose-600"><TrendingDown className="h-3 w-3" />{delta}% vs 上月</span>
}

/* ─── Donut chart ──────────────────────────────────────────── */

interface DonutSlice { key: string; label: string; value: number; color: string }

function DonutChart({ data, centerLabel, centerValue }: { data: DonutSlice[]; centerLabel: string; centerValue: string }) {
  const total = data.reduce((sum, d) => sum + d.value, 0) || 1
  const radius = 70
  const stroke = 22
  const center = 90
  const circumference = 2 * Math.PI * radius

  // Pre-compute each slice's stroke dasharray + offset
  let cumulative = 0
  const slices = data.map(d => {
    const fraction = d.value / total
    const dash = circumference * fraction
    const gap = circumference - dash
    const offset = -cumulative * circumference
    cumulative += fraction
    return { ...d, fraction, dash, gap, offset }
  })

  return (
    <div className="flex flex-col items-center gap-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="relative">
        <svg width={center * 2} height={center * 2} viewBox={`0 0 ${center * 2} ${center * 2}`}>
          {/* track */}
          <circle cx={center} cy={center} r={radius} fill="none" stroke="#f1f5f9" strokeWidth={stroke} />
          {/* slices */}
          <g transform={`rotate(-90 ${center} ${center})`}>
            {slices.map(s => (
              <circle
                key={s.key}
                cx={center}
                cy={center}
                r={radius}
                fill="none"
                stroke={s.color}
                strokeWidth={stroke}
                strokeDasharray={`${s.dash} ${s.gap}`}
                strokeDashoffset={s.offset}
                style={{ transition: 'stroke-dasharray 400ms ease-out' }}
              >
                <title>{`${s.label}: ${s.value} (${Math.round(s.fraction * 100)}%)`}</title>
              </circle>
            ))}
          </g>
        </svg>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <div className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">{centerLabel}</div>
          <div className="font-mono text-2xl font-bold tabular text-ink">{centerValue}</div>
        </div>
      </div>

      <div className="grid w-full grid-cols-1 gap-1.5 sm:max-w-[210px]">
        {slices.map(s => (
          <div key={s.key} className="flex items-center gap-2 text-xs">
            <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: s.color }} />
            <span className="flex-1 truncate text-ink">{s.label}</span>
            <span className="font-mono tabular text-ink-muted">{s.value}</span>
            <span className="w-10 text-right font-mono tabular text-ink-faint">{Math.round(s.fraction * 100)}%</span>
          </div>
        ))}
      </div>
    </div>
  )
}

/* ─── Area + line chart ───────────────────────────────────── */

interface XYPoint { x: string; y: number }

function AreaLineChart({ points }: { points: XYPoint[] }) {
  if (points.length === 0) {
    return <div className="text-center text-sm text-ink-faint">no data</div>
  }
  const W = 720, H = 220
  const padL = 36, padR = 16, padT = 16, padB = 32
  const innerW = W - padL - padR
  const innerH = H - padT - padB
  const maxY = Math.max(...points.map(p => p.y), 1)
  // round max up to a "nice" number for the y-axis ticks
  const niceMax = niceCeil(maxY)
  const xStep = points.length > 1 ? innerW / (points.length - 1) : innerW
  const xy = points.map((p, i) => ({
    x: padL + i * xStep,
    y: padT + innerH - (p.y / niceMax) * innerH,
    raw: p,
  }))

  const linePath = xy.map((p, i) => `${i === 0 ? 'M' : 'L'} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ')
  const areaPath = `${linePath} L ${xy[xy.length - 1].x.toFixed(1)} ${padT + innerH} L ${xy[0].x.toFixed(1)} ${padT + innerH} Z`

  // 5 horizontal grid lines
  const yTicks = Array.from({ length: 5 }, (_, i) => Math.round((niceMax / 4) * i))

  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="w-full" preserveAspectRatio="xMidYMid meet">
      <defs>
        <linearGradient id="areaGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.45" />
          <stop offset="100%" stopColor="#f59e0b" stopOpacity="0" />
        </linearGradient>
      </defs>

      {/* y-axis grid + labels */}
      {yTicks.map(t => {
        const y = padT + innerH - (t / niceMax) * innerH
        return (
          <g key={t}>
            <line x1={padL} y1={y} x2={padL + innerW} y2={y} stroke="#e5e7eb" strokeWidth="1" strokeDasharray={t === 0 ? '' : '2 3'} />
            <text x={padL - 6} y={y + 4} fontSize="10" textAnchor="end" fill="#94a3b8" className="font-mono tabular">{t}</text>
          </g>
        )
      })}

      {/* x-axis labels */}
      {xy.map((p, i) => (
        <text key={i} x={p.x} y={H - 8} fontSize="10.5" textAnchor="middle" fill="#64748b" className="font-mono">
          {p.raw.x.slice(2)}
        </text>
      ))}

      {/* area fill */}
      <path d={areaPath} fill="url(#areaGrad)" />
      {/* line */}
      <path d={linePath} fill="none" stroke="#f59e0b" strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
      {/* points + value labels */}
      {xy.map((p, i) => (
        <g key={i}>
          <circle cx={p.x} cy={p.y} r={4} fill="white" stroke="#f59e0b" strokeWidth="2" />
          <text x={p.x} y={p.y - 9} fontSize="10.5" textAnchor="middle" fill="#1f2937" className="font-mono font-semibold tabular">{p.raw.y}</text>
        </g>
      ))}
    </svg>
  )
}

function niceCeil(n: number): number {
  if (n <= 5) return 5
  if (n <= 10) return 10
  const power = Math.pow(10, Math.floor(Math.log10(n)))
  const fraction = n / power
  let nice: number
  if (fraction <= 1) nice = 1
  else if (fraction <= 2) nice = 2
  else if (fraction <= 5) nice = 5
  else nice = 10
  return nice * power
}
