/**
 * Colour-coded status badge for SpecBundleStatus. Distinct from the generic
 * StatusBadge in components/ui/badge because the bundle status enum is
 * different from the case-status enum (no overlap to share).
 */
import type { SpecBundleStatus } from '@/types/flowLibrary'

const TONES: Record<SpecBundleStatus, { bg: string; fg: string; border: string; label: string }> = {
  draft:               { bg: 'bg-slate-100',  fg: 'text-slate-600',  border: 'border-slate-200', label: 'Draft' },
  pending:             { bg: 'bg-amber-50',   fg: 'text-amber-700',  border: 'border-amber-200', label: 'Pending' },
  installed:           { bg: 'bg-green-50',   fg: 'text-green-700',  border: 'border-green-200', label: 'Installed' },
  installedUnverified: { bg: 'bg-blue-50',    fg: 'text-blue-700',   border: 'border-blue-200',  label: 'Installed (unverified)' },
  failed:              { bg: 'bg-red-50',     fg: 'text-red-700',    border: 'border-red-200',   label: 'Failed' },
  softDeleted:         { bg: 'bg-slate-100',  fg: 'text-slate-500',  border: 'border-slate-200', label: 'Deleted' },
}

export function StatusPill({ status }: { status: SpecBundleStatus }) {
  const t = TONES[status] ?? TONES.draft
  return (
    <span className={[
      'inline-flex items-center gap-1.5 rounded border px-2 py-0.5 text-[11px] font-medium whitespace-nowrap',
      t.bg, t.fg, t.border,
    ].join(' ')}>
      <span className="block h-1.5 w-1.5 rounded-full" style={{ backgroundColor: 'currentColor' }} />
      {t.label}
    </span>
  )
}
