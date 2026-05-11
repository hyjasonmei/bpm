/**
 * Process Admin shell (PR-K1 §1). Mounts a 7-section sidebar; each
 * section's body is a sibling component rendered in the right pane.
 *
 * Why a shell-inside-a-shell rather than a 7th branch in App.tsx's flat
 * AdminScreen union? The Process Admin surface has its own internal
 * navigation (Definitions / Designer / Simulator / …) that we don't want
 * polluting the top-level admin nav. Keeping it self-contained also lets
 * later PRs (K2..K5) swap in real components without touching App.tsx.
 *
 * For PR-K1 only "Definitions" has real content; the other six mount
 * `<ComingSoon>` placeholders.
 */

import { useState } from 'react'
import {
  FileText, Pencil, Play, Activity, CheckCircle, BarChart, Bell,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { ComingSoon } from './ComingSoon'
import { DefinitionsList } from './DefinitionsList'

export type ProcessAdminSection =
  | 'definitions'
  | 'designer'
  | 'simulator'
  | 'live-cases'
  | 'completed-cases'
  | 'reports'
  | 'flow-notifications'

const SECTIONS: Array<{
  key: ProcessAdminSection
  label: string
  icon: React.ReactNode
}> = [
  { key: 'definitions',         label: 'Definitions',         icon: <FileText    className="h-4 w-4" /> },
  { key: 'designer',            label: 'Designer',            icon: <Pencil      className="h-4 w-4" /> },
  { key: 'simulator',           label: 'Simulator',           icon: <Play        className="h-4 w-4" /> },
  { key: 'live-cases',          label: 'Live Cases',          icon: <Activity    className="h-4 w-4" /> },
  { key: 'completed-cases',     label: 'Completed Cases',     icon: <CheckCircle className="h-4 w-4" /> },
  { key: 'reports',             label: 'Reports',             icon: <BarChart    className="h-4 w-4" /> },
  { key: 'flow-notifications',  label: 'Flow Notifications',  icon: <Bell        className="h-4 w-4" /> },
]

const SECTION_LABEL: Record<ProcessAdminSection, string> = Object.fromEntries(
  SECTIONS.map(s => [s.key, s.label]),
) as Record<ProcessAdminSection, string>

interface ProcessAdminShellProps {
  /**
   * Optional initial section. Designer entry point (DefinitionsList's
   * "Open in Designer" / "+ New Flow") will set this once K2 lands;
   * for K1 we accept the prop but the only real screen is Definitions.
   */
  initialSection?: ProcessAdminSection
}

export function ProcessAdminShell({ initialSection }: ProcessAdminShellProps = {}) {
  const [section, setSection] = useState<ProcessAdminSection>(initialSection ?? 'definitions')

  return (
    <div className="space-y-4">
      <header className="flex items-baseline gap-2 border-b border-rule pb-3">
        <h1 className="text-xl font-bold text-ink">Process Admin</h1>
        <span className="text-ink-faint">/</span>
        <span className="text-sm text-ink-muted">{SECTION_LABEL[section]}</span>
      </header>

      <div className="flex gap-4">
        <aside className="w-52 shrink-0">
          <nav className="flex flex-col gap-1 rounded-md border border-rule bg-card p-2">
            {SECTIONS.map(item => (
              <button
                key={item.key}
                onClick={() => setSection(item.key)}
                className={cn(
                  'flex items-center gap-2 rounded px-3 py-2 text-sm font-medium transition-colors',
                  section === item.key
                    ? 'bg-slate-100 text-ink'
                    : 'text-ink-muted hover:bg-slate-50 hover:text-ink',
                )}
              >
                {item.icon}
                <span className="flex-1 text-left">{item.label}</span>
              </button>
            ))}
          </nav>
        </aside>

        <main className="min-w-0 flex-1">
          <SectionBody
            section={section}
            onJumpToDesigner={() => setSection('designer')}
          />
        </main>
      </div>
    </div>
  )
}

interface SectionBodyProps {
  section: ProcessAdminSection
  onJumpToDesigner: () => void
}

function SectionBody({ section, onJumpToDesigner }: SectionBodyProps) {
  switch (section) {
    case 'definitions':
      return <DefinitionsList onOpenDesigner={onJumpToDesigner} />
    case 'designer':
      return (
        <ComingSoon
          section="Designer"
          note="BPMN + form editor — ships in PR-K2 (add-process-admin-ui §3)."
        />
      )
    case 'simulator':
      return (
        <ComingSoon
          section="Simulator"
          note="Dry-run flows against sample inputs — ships in PR-K3."
        />
      )
    case 'live-cases':
      return (
        <ComingSoon
          section="Live Cases"
          note="Running ProcessInstances list + detail — ships in PR-K4."
        />
      )
    case 'completed-cases':
      return (
        <ComingSoon
          section="Completed Cases"
          note="Terminal ProcessInstances + cycle-time metrics — ships in PR-K5."
        />
      )
    case 'reports':
      return (
        <ComingSoon
          section="Reports"
          note="Aggregate stats + bottleneck analysis — ships in PR-K5."
        />
      )
    case 'flow-notifications':
      return (
        <ComingSoon
          section="Flow Notifications"
          note="Notification dispatch audit, filterable by spec — ships in PR-K5."
        />
      )
  }
}
