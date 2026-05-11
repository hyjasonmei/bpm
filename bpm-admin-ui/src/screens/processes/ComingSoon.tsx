/**
 * Placeholder for Process Admin sections that ship in later PRs (K2..K5).
 * Lives in the same folder as ProcessAdminShell so swap-in is a one-line
 * import change once the real screen lands.
 */

import { Hammer } from 'lucide-react'

interface ComingSoonProps {
  /** Section label, e.g. "Designer". Rendered prominently. */
  section: string
  /** Optional 1-line note pointing at the PR/proposal that will deliver this. */
  note?: string
}

export function ComingSoon({ section, note }: ComingSoonProps) {
  return (
    <div className="rounded-lg border border-dashed border-rule bg-card p-12 text-center">
      <Hammer className="mx-auto mb-3 h-8 w-8 text-ink-faint" />
      <p className="text-base font-semibold text-ink">{section}</p>
      <p className="mt-2 text-sm text-ink-muted">
        Coming soon — this section is part of the Process Admin UI roadmap.
      </p>
      {note && (
        <p className="mt-3 text-xs text-ink-faint">{note}</p>
      )}
    </div>
  )
}
