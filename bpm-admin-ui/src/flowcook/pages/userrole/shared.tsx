import { cn } from '@/lib/cn'

export function Section({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <section>
      <header className="mb-2.5 flex items-baseline justify-between">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        {hint && <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{hint}</span>}
      </header>
      {children}
    </section>
  )
}

export function Empty({ children }: { children: React.ReactNode }) {
  return (
    <p className="rounded border border-dashed border-rule bg-bg/50 px-3 py-2 text-xs italic text-ink-muted">
      {children}
    </p>
  )
}

export function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</dt>
      <dd className="mt-1">{children}</dd>
    </div>
  )
}

export function FilterChip({
  label, count, active, onClick,
}: { label: string; count: number; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs transition-colors',
        active
          ? 'border-primary bg-primary text-white'
          : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
      )}
    >
      <span className="font-medium">{label}</span>
      <span className={cn('font-mono text-[10px]', active ? 'text-white/80' : 'text-ink-faint')}>
        {count}
      </span>
    </button>
  )
}

export function Cap({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <span className={cn('font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted', className)}>
      {children}
    </span>
  )
}

export function formatDate(s: string): string {
  try {
    return new Date(s).toISOString().slice(0, 16).replace('T', ' ')
  } catch {
    return s
  }
}

export function formatDateLocal(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
