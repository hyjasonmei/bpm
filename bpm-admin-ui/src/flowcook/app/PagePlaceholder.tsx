import { Construction } from 'lucide-react'

interface PagePlaceholderProps {
  title: string
  description: string
  kicker?: string
}

export function PagePlaceholder({ title, description, kicker }: PagePlaceholderProps) {
  return (
    <div className="relative mx-auto flex h-full max-w-3xl flex-col items-start justify-center px-2 py-12">
      <div className="absolute right-2 top-12 hidden text-primary/15 md:block">
        <Construction className="h-32 w-32" strokeWidth={1.1} />
      </div>

      <div className="mb-3 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        coming soon — {kicker ?? 'tbd'}
      </div>
      <h2 className="text-4xl font-bold leading-[1.1] text-ink">
        {title}
      </h2>
      <p className="mt-5 max-w-xl text-sm leading-relaxed text-ink-muted">
        {description}
      </p>

      <div className="mt-8 inline-flex items-center gap-2 rounded-full border border-rule bg-card px-3 py-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
        <span className="block h-1.5 w-1.5 rounded-full bg-accent"></span>
        Not wired yet
      </div>
    </div>
  )
}
