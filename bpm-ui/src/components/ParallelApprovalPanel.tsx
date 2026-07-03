export type ParallelSlotState = 'pending' | 'approved' | 'rejected' | 'skipped'

export interface ParallelSlotView {
  role?: string
  name: string
  state: ParallelSlotState
  comment?: string
  at?: string
}

interface ParallelApprovalPanelProps {
  /** e.g. "並簽 · 需全部核准" or "門檻 3/5" */
  policyLabel: string
  approvedCount: number
  threshold: number
  slots: ParallelSlotView[]
}

const PILL: Record<ParallelSlotState, { label: string; cls: string }> = {
  pending:  { label: '待簽核',  cls: 'bg-amber-100 text-amber-800' },
  approved: { label: '已核准',  cls: 'bg-green-100 text-green-700' },
  rejected: { label: '已退件',  cls: 'bg-red-100 text-red-700' },
  skipped:  { label: '略過',    cls: 'bg-slate-100 text-slate-400' },
}

/**
 * Case-detail checklist for a parallel-approval (並簽) gateway. Presentational;
 * data comes from the flow's case-detail API. Kept in sync with the BPMN
 * diagram's per-node colours (yellow/green/red/grey).
 */
export function ParallelApprovalPanel({ policyLabel, approvedCount, threshold, slots }: ParallelApprovalPanelProps) {
  const pct = threshold > 0 ? Math.min(100, Math.round((approvedCount / threshold) * 100)) : 0
  return (
    <div className="rounded-lg border border-slate-200 bg-white p-4">
      <div className="mb-1 flex items-center justify-between">
        <h3 className="text-sm font-semibold text-slate-800">並簽進度</h3>
        <span className="text-xs text-slate-500">{policyLabel}</span>
      </div>
      <div className="mb-3 flex items-center gap-2">
        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-200">
          <div className="h-full bg-blue-600 transition-all" style={{ width: `${pct}%` }} />
        </div>
        <span className="text-xs font-medium text-slate-500">{approvedCount}/{threshold}</span>
      </div>
      <ul className="divide-y divide-slate-100">
        {slots.map((s, i) => {
          const pill = PILL[s.state]
          return (
            <li key={i} className={`py-2 ${s.state === 'skipped' ? 'opacity-50' : ''}`}>
              <div className="flex items-center gap-3">
                {s.role && <span className="w-14 shrink-0 text-xs text-slate-500">{s.role}</span>}
                <span className="flex-1 text-sm font-medium text-slate-800">{s.name}</span>
                <span className={`rounded px-2 py-0.5 text-xs font-semibold ${pill.cls}`}>{pill.label}</span>
              </div>
              {(s.comment || s.at) && (
                <div className="mt-0.5 pl-14 text-xs text-slate-400">
                  {s.comment && <span>「{s.comment}」</span>}
                  {s.comment && s.at && <span> · </span>}
                  {s.at && <span>{s.at}</span>}
                </div>
              )}
            </li>
          )
        })}
      </ul>
    </div>
  )
}
