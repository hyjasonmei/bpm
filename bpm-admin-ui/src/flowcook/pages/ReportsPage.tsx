import { useMemo } from 'react'
import { TrendingUp, TrendingDown, Minus, FileText, CheckCircle2, Clock, Activity } from 'lucide-react'
import { cn } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { TypeChip } from '@/components/ui/badge'

/* ─── Self-contained mock data (no API, no bpm-ui imports) ──── */

type FormCode = 'LEAVE' | 'GEE' | 'GEV' | 'APE' | 'HWP' | 'ITPR' | 'TRQ' | 'TEO' | 'EXTOB'

/** Subset of bpm-ui's CaseMock — only the fields Reports actually uses. */
interface CaseMock {
  type: FormCode
  status: string
  submitted: string
  updated: string
}

const MOCK_CASES: CaseMock[] = [
  { type: 'LEAVE', status: 'pending',        submitted: '2026/04/24', updated: '2026/04/24' },
  { type: 'LEAVE', status: 'pending',        submitted: '2026/04/23', updated: '2026/04/23' },
  { type: 'GEE',   status: 'pending',        submitted: '2026/04/22', updated: '2026/04/23' },
  { type: 'GEE',   status: 'pending',        submitted: '2026/04/23', updated: '2026/04/23' },
  { type: 'GEV',   status: 'pending',        submitted: '2026/04/21', updated: '2026/04/21' },
  { type: 'TRQ',   status: 'pending',        submitted: '2026/04/20', updated: '2026/04/20' },
  { type: 'APE',   status: 'pending',        submitted: '2026/04/19', updated: '2026/04/19' },
  { type: 'GEE',   status: 'pending',        submitted: '2026/04/17', updated: '2026/04/17' },
  { type: 'GEV',   status: 'pending',        submitted: '2026/04/13', updated: '2026/04/13' },
  { type: 'GEE',   status: 'pending',        submitted: '2026/04/14', updated: '2026/04/14' },
  { type: 'TEO',   status: 'fin_review',     submitted: '2026/04/01', updated: '2026/04/21' },
  { type: 'TEO',   status: 'fin_review',     submitted: '2026/04/18', updated: '2026/04/18' },
  { type: 'TEO',   status: 'fin_review',     submitted: '2026/04/11', updated: '2026/04/11' },
  { type: 'HWP',   status: 'it_spec_review', submitted: '2026/03/20', updated: '2026/04/01' },
  { type: 'HWP',   status: 'it_spec_review', submitted: '2026/04/15', updated: '2026/04/15' },
  { type: 'LEAVE', status: 'pending',        submitted: '2026/04/15', updated: '2026/04/19' },
  { type: 'EXTOB', status: 'pending',        submitted: '2026/04/05', updated: '2026/04/12' },
  { type: 'APE',   status: 'draft',          submitted: '2026/04/08', updated: '2026/04/08' },
  { type: 'LEAVE', status: 'draft',          submitted: '2026/04/22', updated: '2026/04/22' },
  { type: 'TRQ',   status: 'closed',         submitted: '2026/04/10', updated: '2026/04/15' },
  { type: 'GEE',   status: 'closed',         submitted: '2026/03/28', updated: '2026/04/05' },
  { type: 'GEV',   status: 'closed',         submitted: '2026/03/15', updated: '2026/03/22' },
  { type: 'TRQ',   status: 'closed',         submitted: '2026/03/01', updated: '2026/03/10' },
  { type: 'APE',   status: 'closed',         submitted: '2026/02/10', updated: '2026/02/20' },
  { type: 'ITPR',  status: 'closed',         submitted: '2026/01/20', updated: '2026/02/05' },
  { type: 'LEAVE', status: 'closed',         submitted: '2026/02/14', updated: '2026/02/16' },
  { type: 'GEE',   status: 'approved',       submitted: '2026/02/25', updated: '2026/03/01' },
  { type: 'GEV',   status: 'approved',       submitted: '2026/04/18', updated: '2026/04/20' },
  { type: 'GEE',   status: 'returned',       submitted: '2026/02/01', updated: '2026/02/03' },
]

const FORM_TYPES: FormCode[] = ['LEAVE', 'GEE', 'GEV', 'APE', 'HWP', 'ITPR', 'TRQ', 'TEO', 'EXTOB']
const STATUS_ORDER = ['draft', 'pending', 'approved', 'fin_review', 'it_spec_review', 'returned', 'closed', 'rejected'] as const

const STATUS_COLOR: Record<string, { hex: string; bar: string; bg: string; label: string; fg: string }> = {
  draft:           { hex: '#94a3b8', bar: 'bg-slate-400',   bg: 'bg-slate-100',   fg: 'text-slate-700',   label: 'Draft' },
  pending:         { hex: '#f59e0b', bar: 'bg-amber-500',   bg: 'bg-amber-50',    fg: 'text-amber-700',   label: 'Pending' },
  approved:        { hex: '#3b82f6', bar: 'bg-blue-500',    bg: 'bg-blue-50',     fg: 'text-blue-700',    label: 'Approved' },
  fin_review:      { hex: '#8b5cf6', bar: 'bg-violet-500',  bg: 'bg-violet-50',   fg: 'text-violet-700',  label: 'FIN Review' },
  it_spec_review:  { hex: '#06b6d4', bar: 'bg-cyan-500',    bg: 'bg-cyan-50',     fg: 'text-cyan-700',    label: 'IT Review' },
  returned:        { hex: '#f97316', bar: 'bg-orange-500',  bg: 'bg-orange-50',   fg: 'text-orange-700',  label: 'Returned' },
  closed:          { hex: '#22c55e', bar: 'bg-green-500',   bg: 'bg-green-50',    fg: 'text-green-700',   label: 'Closed' },
  rejected:        { hex: '#ef4444', bar: 'bg-red-500',     bg: 'bg-red-50',      fg: 'text-red-700',     label: 'Rejected' },
  cancelled:       { hex: '#94a3b8', bar: 'bg-slate-400',   bg: 'bg-slate-100',   fg: 'text-slate-500',   label: 'Cancelled' },
}

export function ReportsPage() {
  /* ─── Aggregations ─────────────────────────────────────── */

  const byType = useMemo(() => {
    const out: Record<FormCode, number> = Object.fromEntries(FORM_TYPES.map(t => [t, 0])) as Record<FormCode, number>
    for (const c of MOCK_CASES) out[c.type] = (out[c.type] ?? 0) + 1
    return Object.entries(out)
      .map(([type, count]) => ({ type: type as FormCode, count }))
      .sort((a, b) => b.count - a.count)
  }, [])
  const maxByType = Math.max(...byType.map(b => b.count), 1)

  const byStatus = useMemo(() => {
    const out: Record<string, number> = Object.fromEntries(STATUS_ORDER.map(s => [s, 0]))
    for (const c of MOCK_CASES) out[c.status] = (out[c.status] ?? 0) + 1
    return STATUS_ORDER.map(s => ({ status: s, count: out[s] })).filter(d => d.count > 0)
  }, [])

  const monthly = useMemo(() => {
    const counts: Record<string, number> = {}
    for (const c of MOCK_CASES) {
      const ym = c.submitted.slice(0, 7)
      counts[ym] = (counts[ym] ?? 0) + 1
    }
    const months = Object.keys(counts).sort().slice(-6)
    return months.map(m => ({ month: m, count: counts[m] ?? 0 }))
  }, [])

  /* ─── KPIs ─────────────────────────────────────────────── */

  const kpis = useMemo(() => {
    const total = MOCK_CASES.length
    const thisMonth = monthly[monthly.length - 1]?.count ?? 0
    const lastMonth = monthly[monthly.length - 2]?.count ?? 0
    const monthDelta = lastMonth === 0 ? 0 : Math.round(((thisMonth - lastMonth) / lastMonth) * 100)

    const approvedCount = MOCK_CASES.filter(c => c.status === 'approved' || c.status === 'closed').length
    const rejectedCount = MOCK_CASES.filter(c => c.status === 'rejected').length
    const decided = approvedCount + rejectedCount
    const approvalRate = decided === 0 ? 0 : Math.round((approvedCount / decided) * 100)

    // Avg cycle days = (updated - submitted) for terminal-state cases
    const terminal = MOCK_CASES.filter(c => c.status === 'closed' || c.status === 'rejected' || c.status === 'approved')
    const cycleDays = terminal.length === 0 ? 0 :
      Math.round(terminal.reduce((sum, c) => {
        const start = Date.parse(c.submitted)
        const end = Date.parse(c.updated)
        return sum + Math.max(1, Math.round((end - start) / 86400000))
      }, 0) / terminal.length)

    return { total, thisMonth, monthDelta, approvalRate, cycleDays }
  }, [monthly])

  /* ─── Render ───────────────────────────────────────────── */

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Reports</h1>
          <p className="text-sm text-ink-muted">表單流量統計 — derived from {MOCK_CASES.length} mock cases</p>
        </div>
        <div className="text-[10.5px] uppercase tracking-wider text-ink-faint">
          Reporting period · last 6 months · all departments
        </div>
      </div>

      {/* KPI strip */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Kpi
          icon={<FileText className="h-4 w-4" />}
          label="Total cases"
          value={kpis.total.toString()}
          accent="bg-blue-50 text-blue-700"
          sub={<>累計件數</>}
        />
        <Kpi
          icon={<Activity className="h-4 w-4" />}
          label="This month"
          value={kpis.thisMonth.toString()}
          accent="bg-amber-50 text-amber-700"
          sub={<DeltaTag delta={kpis.monthDelta} />}
        />
        <Kpi
          icon={<CheckCircle2 className="h-4 w-4" />}
          label="Approval rate"
          value={`${kpis.approvalRate}%`}
          accent="bg-green-50 text-green-700"
          sub={<>已決議案件中通過比例</>}
        />
        <Kpi
          icon={<Clock className="h-4 w-4" />}
          label="Avg cycle"
          value={`${kpis.cycleDays}d`}
          accent="bg-violet-50 text-violet-700"
          sub={<>送出 → 結案平均天數</>}
        />
      </div>

      {/* Donut + Type bars */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-5">
        <div className="lg:col-span-2">
          <SectionCard>
            <SectionTitle>Status Breakdown / 狀態分布</SectionTitle>
            <div className="p-5">
              <DonutChart
                data={byStatus.map(s => ({ key: s.status, label: STATUS_COLOR[s.status].label, value: s.count, color: STATUS_COLOR[s.status].hex }))}
                centerLabel="Total"
                centerValue={kpis.total.toString()}
              />
            </div>
          </SectionCard>
        </div>

        <div className="lg:col-span-3">
          <SectionCard>
            <SectionTitle>Counts by Form Type / 各表單數量</SectionTitle>
            <div className="space-y-2.5 p-5">
              {byType.map(({ type, count }) => (
                <div key={type} className="grid grid-cols-[120px_1fr_50px] items-center gap-3">
                  <div className="flex items-center gap-2"><TypeChip type={type} /></div>
                  <div className="h-7 overflow-hidden rounded bg-slate-100">
                    <div
                      className="h-full rounded bg-gradient-to-r from-blue-500 to-blue-400 transition-all"
                      style={{ width: `${(count / maxByType) * 100}%` }}
                    />
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
          <AreaLineChart points={monthly.map(m => ({ x: m.month, y: m.count }))} />
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
