import { Construction } from 'lucide-react'
import { cn } from '@/lib/cn'

export type PhaseId = 'prep' | 'cook' | 'serve'

export interface PhaseDef {
  id: PhaseId
  label: string
  hint?: string
  disabled?: boolean
  disabledHint?: string
}

const PHASES: PhaseDef[] = [
  { id: 'prep',  label: 'Prep',  hint: '備料 — 用 wizard 把 spec 寫清楚' },
  { id: 'cook',  label: 'Cook',  hint: '烹調 — chef 把 spec 編成 runtime artifact', disabled: true, disabledHint: '即將上線（chef pipeline）' },
  { id: 'serve', label: 'Serve', hint: '上菜 — 部署到 bpm runtime',                disabled: true, disabledHint: '即將上線（serve pipeline）' },
]

export function PhaseTabs({
  active,
  onChange,
  phases = PHASES,
}: {
  active: PhaseId
  onChange?: (id: PhaseId) => void
  phases?: PhaseDef[]
}) {
  return (
    <div className="inline-flex items-center gap-1 rounded-md border border-rule bg-card p-0.5">
      {phases.map(p => {
        const isActive = p.id === active
        const title = p.disabled ? (p.disabledHint ?? 'Coming soon') : p.hint
        return (
          <button
            key={p.id}
            onClick={p.disabled ? undefined : () => onChange?.(p.id)}
            disabled={p.disabled}
            title={title}
            className={cn(
              'inline-flex items-center gap-1.5 rounded px-3 py-1.5 text-xs font-semibold transition-colors',
              isActive && 'bg-primary text-white shadow-sm',
              !isActive && !p.disabled && 'text-ink-muted hover:bg-bg hover:text-ink',
              p.disabled && 'cursor-not-allowed text-ink-faint',
            )}
          >
            {p.disabled && <Construction className="h-3 w-3" />}
            <span className="tracking-wide">{p.label}</span>
          </button>
        )
      })}
    </div>
  )
}
