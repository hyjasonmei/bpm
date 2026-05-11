import * as React from 'react'
import { Settings, Users, Sparkles, Shield, Activity, ExternalLink, LogOut, Eye, Library } from 'lucide-react'
import { cn } from '@/lib/cn'
import { decodeJwt } from '@/lib/jwt'
import { clearJwt, getJwt } from '@/lib/apiFetch'
import { SandboxBanner } from '@/components/SandboxBanner'

export type AdminScreen =
  | { kind: 'onboarding' }
  | { kind: 'flow-library' }
  | { kind: 'site-settings' }
  | { kind: 'users-roles' }
  | { kind: 'impersonation' }
  | { kind: 'audit' }

interface AdminLayoutProps {
  screen: AdminScreen
  setScreen: (s: AdminScreen) => void
  children: React.ReactNode
  topBanner?: React.ReactNode
}

const NAV: Array<{ kind: AdminScreen['kind']; label: string; icon: React.ReactNode }> = [
  { kind: 'onboarding',    label: 'Onboarding',    icon: <Sparkles className="h-4 w-4" /> },
  { kind: 'flow-library',  label: 'Flow Library',  icon: <Library  className="h-4 w-4" /> },
  { kind: 'site-settings', label: 'Site Settings', icon: <Settings className="h-4 w-4" /> },
  { kind: 'users-roles',   label: 'Users & Roles', icon: <Users    className="h-4 w-4" /> },
  { kind: 'impersonation', label: 'Impersonation', icon: <Eye      className="h-4 w-4" /> },
  { kind: 'audit',         label: 'Audit Logs',    icon: <Activity className="h-4 w-4" /> },
]

export function AdminLayout({ screen, setScreen, children, topBanner }: AdminLayoutProps) {
  const jwt = getJwt()
  const decoded = jwt ? decodeJwt(jwt) : null
  const fullName = decoded?.full_name ?? decoded?.email ?? 'admin'

  return (
    <div className="min-h-screen bg-bg">
      <SandboxBanner />
      {topBanner}
      {/* Top bar */}
      <header className="sticky top-0 z-40 border-b border-rule bg-slate-900 text-white shadow-sm">
        <div className="flex h-12 items-center px-4">
          <div className="flex items-center gap-2">
            <div className="flex h-7 w-7 items-center justify-center rounded bg-amber-500 text-[10.5px] font-bold tracking-wider text-white">
              <Shield className="h-3.5 w-3.5" />
            </div>
            <span className="text-sm font-bold tracking-wide">BPM Admin Console</span>
          </div>
          <div className="ml-auto flex items-center gap-3">
            <a href="/app/" className="flex items-center gap-1 rounded px-2 py-1 text-xs text-white/80 hover:bg-white/10 hover:text-white">
              <ExternalLink className="h-3.5 w-3.5" />
              Employee App
            </a>
            <span className="text-xs text-white/70">{fullName}</span>
            <button
              onClick={() => { clearJwt(); window.location.reload() }}
              title="Logout"
              className="flex h-8 w-8 items-center justify-center rounded text-white/70 hover:bg-white/10 hover:text-white"
            >
              <LogOut className="h-4 w-4" />
            </button>
          </div>
        </div>
      </header>

      {/* Body: sidebar + content */}
      <div className="flex">
        <aside className="w-56 shrink-0 border-r border-rule bg-card">
          <nav className="sticky top-12 flex flex-col p-2">
            {NAV.map(item => (
              <button
                key={item.kind}
                onClick={() => setScreen({ kind: item.kind } as AdminScreen)}
                className={cn(
                  'flex items-center gap-2 rounded px-3 py-2 text-sm font-medium transition-colors',
                  screen.kind === item.kind
                    ? 'bg-slate-100 text-ink'
                    : 'text-ink-muted hover:bg-slate-50 hover:text-ink',
                )}
              >
                {item.icon}
                {item.label}
              </button>
            ))}
          </nav>
        </aside>
        <main className="flex-1 px-6 py-6">
          {children}
        </main>
      </div>
    </div>
  )
}
