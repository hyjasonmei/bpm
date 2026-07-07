import { useState } from 'react'
import {
  Activity,
  Inbox,
  ArrowLeft,
  BarChart3,
  ChefHat,
  Database,
  ExternalLink,
  FlaskConical,
  PanelLeftClose,
  PanelLeftOpen,
  Settings,
  Stethoscope,
  Users,
} from 'lucide-react'
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { cn } from '@/lib/cn'
import { usePageHeader } from '@/flowcook/app/pageHeader'
import { UserMenu } from '@/flowcook/app/UserMenu'
import { UserRolePage } from '@/flowcook/pages/UserRolePage'
import { AiKitchenPage } from '@/flowcook/pages/AiKitchenPage'
import { SiteSettingPage } from '@/flowcook/pages/SiteSettingPage'
import { ReportsPage } from '@/flowcook/pages/ReportsPage'
import { AuditPage } from '@/flowcook/pages/AuditPage'
import { SandboxPage } from '@/flowcook/pages/SandboxPage'
import { DoctorPage } from '@/flowcook/pages/DoctorPage'
import DatasetsPage from '@/flowcook/pages/DatasetsPage'
import { IssuesPage } from '@/flowcook/pages/IssuesPage'

export type FlowcookPage = 'ai-kitchen' | 'user-role' | 'datasets' | 'sandbox' | 'audit' | 'reports' | 'doctor' | 'issues' | 'site-setting'

interface NavEntry {
  id: FlowcookPage
  path: string
  label: string
  hint: string
  icon: React.ComponentType<{ className?: string }>
}

const NAV: NavEntry[] = [
  { id: 'ai-kitchen',   path: '/ai-kitchen',   label: 'AI Kitchen',   hint: 'flow design',  icon: ChefHat },
  { id: 'user-role',    path: '/user-role',    label: 'User & Role',  hint: 'principals',   icon: Users },
  { id: 'datasets',     path: '/datasets',     label: '資料集 / Datasets', hint: 'reference data', icon: Database },
  { id: 'sandbox',      path: '/sandbox',      label: 'Sandbox',      hint: 'safe testing', icon: FlaskConical },
  { id: 'audit',        path: '/audit',        label: 'Audit',        hint: 'history',      icon: Activity },
  { id: 'reports',      path: '/reports',      label: 'Reports',      hint: 'analytics',    icon: BarChart3 },
  { id: 'doctor',       path: '/doctor',       label: 'Doctor',       hint: 'health',       icon: Stethoscope },
  { id: 'issues',       path: '/issues',       label: '問題回報 / Issues', hint: 'user reports', icon: Inbox },
  { id: 'site-setting', path: '/site-setting', label: 'Site Setting', hint: 'globals',      icon: Settings },
]

const LEGACY_FLAG_KEY = 'flowcook_legacy_visible'
const COLLAPSED_KEY = 'flowcook_sidebar_collapsed'

function readLegacyFlag(): boolean {
  try { return localStorage.getItem(LEGACY_FLAG_KEY) === '1' }
  catch { return false }
}

function readCollapsed(): boolean {
  try { return localStorage.getItem(COLLAPSED_KEY) === '1' }
  catch { return false }
}

function writeCollapsed(v: boolean) {
  try {
    if (v) localStorage.setItem(COLLAPSED_KEY, '1')
    else localStorage.removeItem(COLLAPSED_KEY)
  } catch { /* ignore */ }
}

interface AppShellProps {
  onShowLegacy?: () => void
}

export function AppShell({ onShowLegacy }: AppShellProps) {
  const [collapsed, setCollapsed] = useState<boolean>(readCollapsed)
  const legacyEnabled = readLegacyFlag()
  const location = useLocation()

  const toggleCollapsed = () => {
    setCollapsed((c) => {
      const next = !c
      writeCollapsed(next)
      return next
    })
  }

  // Header label/hint mirrors the active nav entry. Sub-routes (e.g.
  // /ai-kitchen/<flowId>, /user-role/roles) all share the same parent
  // pill so the strip stays stable.
  const current = NAV.find((n) => location.pathname.startsWith(n.path)) ?? NAV[0]

  return (
    <div className="flex h-screen overflow-hidden bg-bg text-ink">
      <aside
        className={cn(
          'flex shrink-0 flex-col bg-header text-white shadow-md transition-[width] duration-200',
          collapsed ? 'w-14' : 'w-64',
        )}
      >
        {/* brand + collapse toggle — h-16 matches the top header strip */}
        <div className={cn(
          'flex h-16 shrink-0 items-center border-b border-white/10',
          collapsed ? 'justify-center px-2' : 'justify-between px-5',
        )}>
          {!collapsed && (
            <div className="flex items-center gap-2.5">
              <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded bg-red-500 text-white">
                <ChefHat className="h-4 w-4" />
              </div>
              <div>
                <div className="text-sm font-bold tracking-wide leading-none">
                  flowcook · admin
                </div>
                <div className="mt-1 font-mono text-[10px] tracking-[0.14em] uppercase text-white/45">
                  1.0.0
                </div>
              </div>
            </div>
          )}
          <button
            onClick={toggleCollapsed}
            data-testid="sidebar-collapse"
            title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            className="flex h-8 w-8 items-center justify-center rounded text-white/55 transition-colors hover:bg-white/10 hover:text-white"
          >
            {collapsed
              ? <PanelLeftOpen className="h-4 w-4" />
              : <PanelLeftClose className="h-3.5 w-3.5" />}
          </button>
        </div>

        {/* nav */}
        <nav className={cn('flex-1 py-3', collapsed ? 'px-1.5' : 'px-2')}>
          {!collapsed && (
            <div className="mb-1.5 px-3 font-mono text-[10px] tracking-[0.14em] uppercase text-white/45">
              menu
            </div>
          )}
          {NAV.map((item) => {
            const Icon = item.icon
            return (
              <NavLink
                key={item.id}
                to={item.path}
                data-testid={`nav-${item.id}`}
                title={collapsed ? `${item.label} — ${item.hint}` : undefined}
                className={({ isActive }) => cn(
                  'group mb-0.5 flex w-full items-center rounded text-left transition-colors',
                  collapsed ? 'justify-center px-0 py-2.5' : 'gap-3 px-3 py-2',
                  isActive
                    ? 'bg-white/20 text-white'
                    : 'text-white/80 hover:bg-white/10 hover:text-white',
                )}
              >
                <Icon className="h-4 w-4 shrink-0" />
                {!collapsed && (
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium leading-none">{item.label}</div>
                    <div className="mt-1 text-[10.5px] tracking-wider text-white/50">
                      {item.hint}
                    </div>
                  </div>
                )}
              </NavLink>
            )
          })}

          {legacyEnabled && onShowLegacy && !collapsed && (
            <button
              onClick={onShowLegacy}
              className="mt-6 flex w-full items-center gap-2 rounded border border-dashed border-white/20 px-3 py-2 font-mono text-[10px] tracking-[0.14em] uppercase text-white/55 hover:bg-white/5 hover:text-white"
            >
              <ExternalLink className="h-3 w-3" />
              <span>Legacy admin</span>
            </button>
          )}
        </nav>

      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {/* page header strip — pages may override via PageHeaderProvider */}
        <PageHeaderStrip fallbackLabel={current.label} fallbackHint={current.hint} />

        <main className="flex-1 overflow-auto px-8 py-6">
          <Routes>
            <Route path="/" element={<Navigate to="/ai-kitchen" replace />} />
            <Route path="/ai-kitchen/*" element={<AiKitchenPage />} />
            <Route path="/user-role/*" element={<UserRolePage />} />
            <Route path="/datasets" element={<DatasetsPage />} />
            <Route path="/sandbox" element={<SandboxPage />} />
            <Route path="/audit" element={<AuditPage />} />
            <Route path="/reports" element={<ReportsPage />} />
            <Route path="/doctor" element={<DoctorPage />} />
            <Route path="/issues" element={<IssuesPage />} />
            <Route path="/site-setting/*" element={<SiteSettingPage />} />
            <Route path="*" element={<Navigate to="/ai-kitchen" replace />} />
          </Routes>
        </main>
      </div>
    </div>
  )
}

function PageHeaderStrip({ fallbackLabel, fallbackHint }: { fallbackLabel: string; fallbackHint: string }) {
  const override = usePageHeader()

  // Brand kicker lives in the sidebar; this strip is one row so its
  // bottom border lines up with the sidebar's brand divider.
  return (
    <header className="flex h-16 shrink-0 items-center justify-between gap-4 border-b border-rule bg-card px-8">
      <div className="min-w-0 flex-1">
        {override ? (
          <div className="flex flex-wrap items-center gap-3">
            {override.back && (
              <button
                onClick={override.back.onClick}
                className="inline-flex items-center gap-1 rounded border border-rule bg-card px-2 py-1 text-[11px] font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary"
              >
                <ArrowLeft className="h-3 w-3" />
                {override.back.label}
              </button>
            )}
            <h1 className="truncate text-lg font-bold leading-none text-ink">
              {override.title}
            </h1>
            {override.subtitle && (
              <span className="font-mono text-[11px] text-ink-muted">{override.subtitle}</span>
            )}
            {override.badges}
          </div>
        ) : (
          <div className="flex flex-wrap items-baseline gap-3">
            <h1 className="text-lg font-bold leading-none text-ink">{fallbackLabel}</h1>
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              {fallbackHint}
            </span>
          </div>
        )}
      </div>
      <div className="flex shrink-0 items-center gap-3">
        {override?.status && (
          <div className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            {override.status}
          </div>
        )}
        <UserMenu />
      </div>
    </header>
  )
}
