import { cn } from '@/lib/cn'

export function ReadonlyField({ label, value, mono, className }: { label: string; value: React.ReactNode; mono?: boolean; className?: string }) {
  return (
    <div className={cn('flex flex-col gap-0.5', className)}>
      <div className="text-xs text-ink-muted">{label}</div>
      <div className={cn('text-sm text-ink', mono && 'font-mono font-medium')}>{value || <span className="text-ink-faint">—</span>}</div>
    </div>
  )
}

export interface HistoryRow {
  time: string
  action: string
  by: string
  dept: string
  remark?: string
}

export function HistoryLog({ rows }: { rows: HistoryRow[] }) {
  return (
    <div className="overflow-hidden rounded-lg border border-rule bg-card">
      <div className="border-b border-rule bg-slate-50 px-4 py-2.5 text-sm font-semibold text-ink">History Log</div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-rule bg-slate-50">
              {['Time', 'Action', 'Action Taken by', "Action Taker's Department", 'Remark'].map(h => (
                <th key={h} className="px-4 py-2 text-left text-xs font-medium text-ink-muted">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={i} className="border-b border-slate-100 hover:bg-slate-50/60">
                <td className="whitespace-nowrap px-4 py-2 font-mono text-xs text-ink-muted">{r.time}</td>
                <td className="px-4 py-2 font-medium text-ink">{r.action}</td>
                <td className="px-4 py-2 text-ink-muted">{r.by}</td>
                <td className="px-4 py-2 text-xs text-ink-muted">{r.dept}</td>
                <td className="px-4 py-2 text-xs text-ink-muted">{r.remark ?? ''}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

export function UploadZone() {
  return (
    <label className="flex cursor-pointer flex-col items-center gap-2 rounded-md border-2 border-dashed border-rule p-6 transition-colors hover:border-blue-300 hover:bg-blue-50/30">
      <span className="text-3xl text-ink-faint">☁️</span>
      <p className="text-sm text-blue-500">Click or Drop file here</p>
      <p className="text-xs text-ink-faint">cannot exceed 10MB</p>
      <input type="file" multiple className="hidden" />
    </label>
  )
}
