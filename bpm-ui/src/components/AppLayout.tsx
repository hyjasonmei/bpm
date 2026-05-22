import * as React from 'react'
import { Plus, Search, BarChart3, Home, ChevronDown, Clock, FlaskConical } from 'lucide-react'
import { cn } from '@/lib/cn'
import { RoleSwitcher } from '@/components/RoleSwitcher'
import { ImpersonationBanner } from '@/components/ImpersonationBanner'
import { SandboxBanner } from '@/components/SandboxBanner'
import { NotificationsMenu } from '@/components/NotificationsMenu'
import { HelpReportMenu } from '@/components/HelpReportMenu'
import type { PersonaCode } from '@/lib/role'
import { FORMS, type FormCode } from '@/lib/workflow'
import { getSandboxStatus } from '@/lib/api/sandbox'

export type Screen =
  | { kind: 'home' }
  | { kind: 'create' }
  | { kind: 'search' }
  | { kind: 'report' }
  | { kind: 'attendance' }
  | { kind: 'sandbox-mailbox' }
  /**
   * Form screen. `taskId` makes it a task-mode form (read-only fields, runtime
   * Approve/Reject/Return); without it the form is in create-mode (default).
   * PR-L2 wires the prop plumbing; PR-L3 wires inbox click → task screen.
   */
  | { kind: 'form'; code: FormCode; taskId?: string }

interface AppLayoutProps {
  screen: Screen
  setScreen: (s: Screen) => void
  persona: PersonaCode
  setPersona: (p: PersonaCode) => void | Promise<void>
  authedFullName?: string | null
  authPending?: boolean
  authError?: string | null
  children: React.ReactNode
}

export const FORM_GROUPS: Array<{ group: string; items: { id: FormCode; label: string }[] }> = [
  { group: 'HR', items: [{ id: 'LEAVE', label: 'Leave Request (請假)' }, { id: 'EXTOB', label: 'External Onboarding' }, { id: 'RESIGN', label: 'Resignation (離職申請)' }, { id: 'DEPTX', label: 'Department Transfer (部門異動)' }] },
  { group: 'Expense', items: [{ id: 'GEE', label: 'Employee Expense (GEE)' }, { id: 'GEV', label: 'Vendor Expense (GEV)' }, { id: 'APE', label: 'Advance Payment (APE)' }] },
  { group: 'Travel', items: [{ id: 'TRQ', label: 'Travel Request (TRQ)' }, { id: 'TEO', label: 'Travel Expense (TEO)' }] },
  { group: 'Purchase', items: [{ id: 'HWP', label: 'Hardware Purchase' }, { id: 'ITPR', label: 'IT Purchase Request' }] },
]

export function AppLayout({ screen, setScreen, persona, setPersona, authedFullName = null, authPending = false, authError = null, children }: AppLayoutProps) {
  const [sandboxOn, setSandboxOn] = React.useState(false)

  // PR-J5 §10.5: Sandbox Mailbox link only visible when sandbox is on. Poll
  // every 30s so a toggle in admin-ui surfaces here without a hard reload.
  React.useEffect(() => {
    let cancelled = false
    async function tick() {
      try {
        const s = await getSandboxStatus()
        if (!cancelled) setSandboxOn(s.enabled)
      } catch { /* swallow */ }
    }
    void tick()
    const handle = window.setInterval(tick, 30_000)
    return () => { cancelled = true; window.clearInterval(handle) }
  }, [])

  return (
    <div className="min-h-screen bg-bg">
      <SandboxBanner />
      <ImpersonationBanner />
      {/* ── Header ────────────────────────────────────────── */}
      <header className="sticky top-0 z-40 bg-header text-white shadow-md">
        <div className="mx-auto flex h-12 max-w-screen-2xl items-center gap-2 px-4">
          {/* Logo */}
          <button onClick={() => setScreen({ kind: 'home' })} className="mr-4 flex items-center gap-2 transition-opacity hover:opacity-90">
            <div className="flex h-7 w-7 items-center justify-center rounded bg-red-500 text-[10.5px] font-bold tracking-wider text-white">BPM</div>
            <span className="text-sm font-bold tracking-wide">BPM System</span>
          </button>

          {/* Nav */}
          <div className="flex items-center gap-0.5">
            <NavBtn active={screen.kind === 'home'} onClick={() => setScreen({ kind: 'home' })} icon={<Home className="h-4 w-4" />}>Home</NavBtn>
            <NavBtn active={screen.kind === 'create'} onClick={() => setScreen({ kind: 'create' })} icon={<Plus className="h-4 w-4" />}>Create</NavBtn>
            <NavBtn active={screen.kind === 'search'} onClick={() => setScreen({ kind: 'search' })} icon={<Search className="h-4 w-4" />}>Search</NavBtn>
            <NavBtn active={screen.kind === 'report'} onClick={() => setScreen({ kind: 'report' })} icon={<BarChart3 className="h-4 w-4" />}>Report</NavBtn>
          </div>

          {/* Right side */}
          <div className="ml-auto flex items-center gap-1">
            {sandboxOn && (
              <NavBtn
                active={screen.kind === 'sandbox-mailbox'}
                onClick={() => setScreen({ kind: 'sandbox-mailbox' })}
                icon={<FlaskConical className="h-4 w-4" />}
              >
                Sandbox
              </NavBtn>
            )}
            <NavBtn active={screen.kind === 'attendance'} onClick={() => setScreen({ kind: 'attendance' })} icon={<Clock className="h-4 w-4" />}>Attendance</NavBtn>
            <NotificationsMenu />
            <HelpReportMenu />
            <RoleSwitcher
              active={persona}
              onChange={setPersona}
              pending={authPending}
              error={authError}
              authedFullName={authedFullName}
            />
          </div>
        </div>

        {/* Stepper bar slot — forms render here via children + portal-like gap */}
        {screen.kind === 'form' && (
          <div className="border-t border-white/10 bg-white/5 px-4">
            <div className="mx-auto max-w-screen-2xl">
              <FormSubHeader code={screen.code} />
            </div>
          </div>
        )}
      </header>

      {/* ── Main content ────────────────────────────────── */}
      <main className="mx-auto max-w-screen-2xl px-4 py-5 fade-in">
        {children}
      </main>
    </div>
  )
}

function NavBtn({ active, onClick, icon, children, hasChevron }: {
  active?: boolean
  onClick?: () => void
  icon?: React.ReactNode
  children: React.ReactNode
  hasChevron?: boolean
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded px-3 py-1.5 text-sm font-medium transition-colors',
        active ? 'bg-white/20 text-white' : 'text-white/80 hover:bg-white/10 hover:text-white',
      )}
    >
      {icon}
      {children}
      {hasChevron && <ChevronDown className="h-3.5 w-3.5 opacity-70" />}
    </button>
  )
}

function FormSubHeader({ code }: { code: FormCode }) {
  // Stepper is rendered inside the form screen itself.
  // This sub-header just shows a thin breadcrumb for context.
  const def = FORMS[code]
  return (
    <div className="flex items-center gap-3 py-1.5 text-[11px] text-white/60">
      <span className="font-mono uppercase tracking-wider">FORM</span>
      <span className="text-white/40">/</span>
      <span className="font-mono uppercase tracking-wider text-white/85">{def.code}</span>
      <span className="text-white/40">·</span>
      <span>{def.label}</span>
      <span className="text-white/40">·</span>
      <span>{def.zhLabel}</span>
    </div>
  )
}
