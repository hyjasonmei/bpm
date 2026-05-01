import { useMemo } from 'react'
import { cn } from '@/lib/cn'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { TypeChip } from '@/components/ui/badge'
import { MOCK_CASES } from '@/lib/mocks'
import type { FormCode } from '@/lib/workflow'
import type { StatusKind } from '@/components/ui/badge'

const FORM_TYPES: FormCode[] = ['LEAVE', 'GEE', 'GEV', 'APE', 'HWP', 'ITPR', 'TRQ', 'TEO', 'EXTOB']
const STATUS_ORDER: StatusKind[] = ['draft', 'pending', 'approved', 'fin_review', 'it_spec_review', 'returned', 'closed', 'rejected']

const STATUS_COLOR: Record<StatusKind, { bar: string; bg: string; label: string; fg: string }> = {
  draft:           { bar: 'bg-slate-400',   bg: 'bg-slate-100',   fg: 'text-slate-700',   label: 'Draft' },
  pending:         { bar: 'bg-amber-500',   bg: 'bg-amber-50',    fg: 'text-amber-700',   label: 'Pending' },
  approved:        { bar: 'bg-blue-500',    bg: 'bg-blue-50',     fg: 'text-blue-700',    label: 'Approved' },
  fin_review:      { bar: 'bg-violet-500',  bg: 'bg-violet-50',   fg: 'text-violet-700',  label: 'FIN Review' },
  it_spec_review:  { bar: 'bg-cyan-500',    bg: 'bg-cyan-50',     fg: 'text-cyan-700',    label: 'IT Review' },
  returned:        { bar: 'bg-orange-500',  bg: 'bg-orange-50',   fg: 'text-orange-700',  label: 'Returned' },
  closed:          { bar: 'bg-green-500',   bg: 'bg-green-50',    fg: 'text-green-700',   label: 'Closed' },
  rejected:        { bar: 'bg-red-500',     bg: 'bg-red-50',      fg: 'text-red-700',     label: 'Rejected' },
}

export function Report() {
  const byType = useMemo(() => {
    const out: Record<FormCode, number> = Object.fromEntries(FORM_TYPES.map(t => [t, 0])) as Record<FormCode, number>
    for (const c of MOCK_CASES) out[c.type] = (out[c.type] ?? 0) + 1
    return Object.entries(out)
      .map(([type, count]) => ({ type: type as FormCode, count }))
      .sort((a, b) => b.count - a.count)
  }, [])

  const maxByType = Math.max(...byType.map(b => b.count), 1)

  const byStatus = useMemo(() => {
    const out: Record<StatusKind, number> = Object.fromEntries(STATUS_ORDER.map(s => [s, 0])) as Record<StatusKind, number>
    for (const c of MOCK_CASES) out[c.status] = (out[c.status] ?? 0) + 1
    return STATUS_ORDER.map(s => ({ status: s, count: out[s] }))
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

  const maxMonthly = Math.max(...monthly.map(m => m.count), 1)

  return (
    <div className="space-y-4">
      <div className="flex items-end justify-between">
        <div>
          <h1 className="text-lg font-bold text-ink">Report</h1>
          <p className="text-sm text-ink-muted">表單流量統計 — derived from {MOCK_CASES.length} mock cases</p>
        </div>
      </div>

      {/* Counts by Form Type */}
      <SectionCard>
        <SectionTitle>Counts by Form Type / 各表單數量</SectionTitle>
        <div className="space-y-2.5 p-5">
          {byType.map(({ type, count }) => (
            <div key={type} className="grid grid-cols-[120px_1fr_50px] items-center gap-3">
              <div className="flex items-center gap-2"><TypeChip type={type} /></div>
              <div className="h-7 rounded bg-slate-100">
                <div className="h-full rounded bg-blue-500/85 transition-all"
                  style={{ width: `${(count / maxByType) * 100}%` }} />
              </div>
              <div className="text-right font-mono text-sm font-semibold tabular text-ink">{count}</div>
            </div>
          ))}
        </div>
      </SectionCard>

      {/* Counts by Status — stacked bar */}
      <SectionCard>
        <SectionTitle>Counts by Status / 各狀態分布</SectionTitle>
        <div className="space-y-3 p-5">
          <div className="flex h-9 overflow-hidden rounded-lg border border-rule">
            {byStatus.map(({ status, count }) => count === 0 ? null : (
              <div
                key={status}
                title={`${STATUS_COLOR[status].label}: ${count}`}
                className={cn('flex h-full items-center justify-center text-[11px] font-semibold text-white', STATUS_COLOR[status].bar)}
                style={{ flex: count }}
              >
                {count}
              </div>
            ))}
          </div>
          <div className="flex flex-wrap gap-x-5 gap-y-1.5 text-xs">
            {byStatus.map(({ status, count }) => (
              <div key={status} className="inline-flex items-center gap-1.5">
                <span className={cn('h-2.5 w-2.5 rounded-sm', STATUS_COLOR[status].bar)} />
                <span className={STATUS_COLOR[status].fg}>{STATUS_COLOR[status].label}</span>
                <span className="font-mono text-ink-faint">{count}</span>
              </div>
            ))}
          </div>
        </div>
      </SectionCard>

      {/* Monthly Volume */}
      <SectionCard>
        <SectionTitle>Monthly Volume / 月份送件量 (last 6 months)</SectionTitle>
        <div className="p-5">
          <div className="grid grid-cols-6 items-end gap-3">
            {monthly.map(m => (
              <div key={m.month} className="flex flex-col items-center gap-1">
                <div className="text-xs font-mono font-semibold text-ink">{m.count}</div>
                <div className="w-full bg-slate-100 rounded-md overflow-hidden flex flex-col-reverse" style={{ height: 120 }}>
                  <div className="bg-amber-500 transition-all" style={{ height: `${(m.count / maxMonthly) * 100}%` }} />
                </div>
                <div className="text-[10.5px] text-ink-muted font-mono">{m.month}</div>
              </div>
            ))}
          </div>
        </div>
      </SectionCard>
    </div>
  )
}
