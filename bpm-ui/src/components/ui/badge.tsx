import { cn } from '@/lib/cn'

export type StatusKind =
  | 'draft' | 'pending' | 'approved' | 'fin_review' | 'it_spec_review' | 'returned' | 'closed' | 'rejected' | 'cancelled'
  // Per-stage kinds so a flow's mid-chain "pending" state shows its real stage
  // name (not a borrowed FIN Review / IT Spec Review label).
  | 'setup' | 'handover' | 'confirm' | 'verification' | 'procurement' | 'sign' | 'hr_record'

const STATUS = {
  draft:           { bg: 'bg-slate-100',  fg: 'text-slate-600',  border: 'border-slate-200', en: 'Draft',          zh: '草稿' },
  pending:         { bg: 'bg-amber-50',   fg: 'text-amber-700',  border: 'border-amber-200', en: 'Pending',        zh: '待簽核' },
  approved:        { bg: 'bg-blue-50',    fg: 'text-blue-700',   border: 'border-blue-200',  en: 'Approved',       zh: '已簽核' },
  fin_review:      { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'FIN Review',     zh: '財務審查' },
  it_spec_review:  { bg: 'bg-cyan-50',    fg: 'text-cyan-700',   border: 'border-cyan-200',  en: 'IT Spec Review', zh: 'IT 審查' },
  returned:        { bg: 'bg-orange-50',  fg: 'text-orange-700', border: 'border-orange-200',en: 'Returned',       zh: '退件' },
  closed:          { bg: 'bg-green-50',   fg: 'text-green-700',  border: 'border-green-200', en: 'Closed',         zh: '結案' },
  rejected:        { bg: 'bg-red-50',     fg: 'text-red-700',    border: 'border-red-200',   en: 'Rejected',       zh: '拒絕' },
  cancelled:       { bg: 'bg-slate-100',  fg: 'text-slate-500',  border: 'border-slate-300', en: 'Cancelled',      zh: '已撤回' },
  setup:           { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'Setup',          zh: '基本設定' },
  handover:        { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'Handover',       zh: '交接' },
  confirm:         { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'Confirm',        zh: '領收確認' },
  verification:    { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'Verification',   zh: '驗收' },
  procurement:     { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'Procurement',    zh: '採購審定' },
  sign:            { bg: 'bg-cyan-50',    fg: 'text-cyan-700',   border: 'border-cyan-200',  en: 'Sign',           zh: '簽核' },
  hr_record:       { bg: 'bg-violet-50',  fg: 'text-violet-700', border: 'border-violet-200',en: 'HR Record',      zh: '人資登錄' },
} as const

export function StatusBadge({ kind, className, withZh = false }: { kind: StatusKind; className?: string; withZh?: boolean }) {
  const s = STATUS[kind]
  return (
    <span className={cn(
      'inline-flex items-center gap-1.5 rounded border px-2 py-0.5 text-xs font-medium whitespace-nowrap',
      s.bg, s.fg, s.border, className,
    )}>
      <span className={cn('block h-1.5 w-1.5 rounded-full', s.fg)} style={{ backgroundColor: 'currentColor' }} />
      {s.en}
      {withZh && <span className="text-[11px] opacity-80">{s.zh}</span>}
    </span>
  )
}

/** Type chip used in lists ("GEE", "GEV"...) */
export function TypeChip({ type, className }: { type: string; className?: string }) {
  return (
    <span className={cn('inline-flex items-center rounded-md bg-slate-100 px-1.5 py-0.5 font-mono text-[10.5px] font-semibold tracking-wide text-slate-700', className)}>
      {type}
    </span>
  )
}

/** Generic Badge for ad-hoc tags */
export function Badge({ tone = 'default', children, className }: {
  tone?: 'default' | 'good' | 'warn' | 'danger' | 'info'
  children: React.ReactNode
  className?: string
}) {
  const tones = {
    default: 'bg-slate-100 text-slate-600',
    good:    'bg-green-50 text-green-700 border border-green-200',
    warn:    'bg-amber-50 text-amber-700 border border-amber-200',
    danger:  'bg-red-50 text-red-700 border border-red-200',
    info:    'bg-blue-50 text-blue-700 border border-blue-200',
  }
  return <span className={cn('inline-flex items-center rounded px-2 py-0.5 text-xs font-medium', tones[tone], className)}>{children}</span>
}
