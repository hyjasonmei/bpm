import { useState } from 'react'
import {
  Activity,
  ChefHat,
  ExternalLink,
  FlaskConical,
  LogOut,
  Settings,
  Users,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import { useAuth } from '@/flowcook/auth/useAuth'
import { PagePlaceholder } from '@/flowcook/app/PagePlaceholder'
import { UserRolePage } from '@/flowcook/pages/UserRolePage'

export type FlowcookPage = 'ai-kitchen' | 'user-role' | 'sandbox' | 'audit' | 'site-setting'

const NAV: Array<{
  id: FlowcookPage
  label: string
  hint: string
  icon: React.ComponentType<{ className?: string }>
}> = [
  { id: 'ai-kitchen',   label: 'AI Kitchen',   hint: 'flow design',  icon: ChefHat },
  { id: 'user-role',    label: 'User & Role',  hint: 'principals',   icon: Users },
  { id: 'sandbox',      label: 'Sandbox',      hint: 'safe testing', icon: FlaskConical },
  { id: 'audit',        label: 'Audit',        hint: 'history',      icon: Activity },
  { id: 'site-setting', label: 'Site Setting', hint: 'globals',      icon: Settings },
]

const LEGACY_FLAG_KEY = 'flowcook_legacy_visible'

function readLegacyFlag(): boolean {
  try {
    return localStorage.getItem(LEGACY_FLAG_KEY) === '1'
  } catch {
    return false
  }
}

interface AppShellProps {
  onShowLegacy?: () => void
}

export function AppShell({ onShowLegacy }: AppShellProps) {
  const { user, logout } = useAuth()
  const [page, setPage] = useState<FlowcookPage>('user-role')
  const legacyEnabled = readLegacyFlag()

  const current = NAV.find((n) => n.id === page) ?? NAV[1]

  let body: React.ReactNode
  switch (page) {
    case 'ai-kitchen':
      body = (
        <PagePlaceholder
          title="AI Kitchen"
          kicker="step 3"
          description="The eleven-step wizard where customer admins prep a flow — source, trigger & access, variables, forms, decisions, approvers, notify, integrations, SLA, translation, notes. Comes online with flowcook-step3-ai-kitchen-wizard."
        />
      )
      break
    case 'user-role':
      body = <UserRolePage />
      break
    case 'sandbox':
      body = (
        <PagePlaceholder
          title="Sandbox"
          kicker="step 4-6"
          description="Three quiet controls — scope, mail intercept, clock freeze — that flip bpm runtime into safe-tasting mode. Wires up once Step 4 (bpm refactor) and Step 6 (syncer) land."
        />
      )
      break
    case 'audit':
      body = (
        <PagePlaceholder
          title="Audit"
          kicker="step 6"
          description="Read-only event ledger. Every action across admin, bpm, chef, syncer lands here as an append-only event. The viewer opens with Step 6 once syncer carries bpm events back."
        />
      )
      break
    case 'site-setting':
      body = (
        <PagePlaceholder
          title="Site Setting"
          kicker="incremental"
          description="Shared configuration — admin SMTP, Anthropic API key, persona-switch allow-list, bpm timezone, default language, tenant branding. Each tab arrives as the feature behind it ships."
        />
      )
      break
  }

  return (
    <div className="flex min-h-screen bg-bg text-ink">
      <aside className="flex w-64 shrink-0 flex-col bg-header text-white shadow-md">
        {/* brand */}
        <div className="border-b border-white/10 px-5 py-4">
          <div className="flex items-center gap-2.5">
            <div className="flex h-8 w-8 items-center justify-center rounded bg-red-500 text-[10.5px] font-bold tracking-wider text-white">
              BPM
            </div>
            <div>
              <div className="text-sm font-bold tracking-wide leading-none">
                flowcook · admin
              </div>
              <div className="mt-1 font-mono text-[10px] tracking-[0.14em] uppercase text-white/45">
                v0
              </div>
            </div>
          </div>
        </div>

        {/* nav */}
        <nav className="flex-1 px-2 py-3">
          <div className="mb-1.5 px-3 font-mono text-[10px] tracking-[0.14em] uppercase text-white/45">
            menu
          </div>
          {NAV.map((item) => {
            const Icon = item.icon
            const active = item.id === page
            return (
              <button
                key={item.id}
                onClick={() => setPage(item.id)}
                data-testid={`nav-${item.id}`}
                className={cn(
                  'group mb-0.5 flex w-full items-center gap-3 rounded px-3 py-2 text-left transition-colors',
                  active
                    ? 'bg-white/20 text-white'
                    : 'text-white/80 hover:bg-white/10 hover:text-white',
                )}
              >
                <Icon className="h-4 w-4 shrink-0" />
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-medium leading-none">{item.label}</div>
                  <div className="mt-1 text-[10.5px] tracking-wider text-white/50">
                    {item.hint}
                  </div>
                </div>
              </button>
            )
          })}

          {legacyEnabled && onShowLegacy && (
            <button
              onClick={onShowLegacy}
              className="mt-6 flex w-full items-center gap-2 rounded border border-dashed border-white/20 px-3 py-2 font-mono text-[10px] tracking-[0.14em] uppercase text-white/55 hover:bg-white/5 hover:text-white"
            >
              <ExternalLink className="h-3 w-3" />
              <span>Legacy admin</span>
            </button>
          )}
        </nav>

        {/* footer / user */}
        <div className="border-t border-white/10 px-5 py-4">
          <div className="flex items-center justify-between">
            <div className="min-w-0">
              <div className="font-mono text-[10px] tracking-[0.14em] uppercase text-white/45">
                signed in as
              </div>
              <div className="mt-0.5 truncate text-sm font-medium text-white">
                {user?.displayName ?? 'admin'}
              </div>
            </div>
            <button
              onClick={() => void logout()}
              title="Sign out"
              className="flex h-8 w-8 items-center justify-center rounded text-white/60 transition-colors hover:bg-white/10 hover:text-white"
            >
              <LogOut className="h-4 w-4" />
            </button>
          </div>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {/* page header strip */}
        <header className="flex items-end justify-between border-b border-rule bg-card px-8 pb-4 pt-6">
          <div>
            <div className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              flowcook · admin
            </div>
            <h1 className="mt-1.5 text-2xl font-bold leading-none text-ink">
              {current.label}
            </h1>
          </div>
          <div className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            {current.hint}
          </div>
        </header>

        <main className="flex-1 overflow-auto px-8 py-6">
          {body}
        </main>
      </div>
    </div>
  )
}
