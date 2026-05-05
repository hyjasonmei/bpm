import * as React from 'react'
import { Bell, HelpCircle, Plus, Search, FileText, BarChart3, Home, ChevronDown, Sparkles } from 'lucide-react'
import { cn } from '@/lib/cn'
import { RoleSwitcher } from '@/components/RoleSwitcher'
import type { PersonaCode } from '@/lib/role'
import { FORMS, type FormCode } from '@/lib/workflow'

export type Screen =
  | { kind: 'home' }
  | { kind: 'search' }
  | { kind: 'report' }
  | { kind: 'onboarding' }
  | { kind: 'form'; code: FormCode }

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

const FORM_GROUPS: Array<{ group: string; items: { id: FormCode; label: string }[] }> = [
  { group: 'HR', items: [{ id: 'LEAVE', label: 'Leave Request (請假)' }, { id: 'EXTOB', label: 'External Onboarding' }] },
  { group: 'Expense', items: [{ id: 'GEE', label: 'Employee Expense (GEE)' }, { id: 'GEV', label: 'Vendor Expense (GEV)' }, { id: 'APE', label: 'Advance Payment (APE)' }] },
  { group: 'Travel', items: [{ id: 'TRQ', label: 'Travel Request (TRQ)' }, { id: 'TEO', label: 'Travel Expense (TEO)' }] },
  { group: 'Purchase', items: [{ id: 'HWP', label: 'Hardware Purchase' }, { id: 'ITPR', label: 'IT Purchase Request' }] },
]

export function AppLayout({ screen, setScreen, persona, setPersona, authedFullName = null, authPending = false, authError = null, children }: AppLayoutProps) {
  const [createOpen, setCreateOpen] = React.useState(false)
  const createRef = React.useRef<HTMLDivElement>(null)

  React.useEffect(() => {
    if (!createOpen) return
    const onDocClick = (e: MouseEvent) => {
      if (createRef.current && !createRef.current.contains(e.target as Node)) setCreateOpen(false)
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [createOpen])

  return (
    <div className="min-h-screen bg-bg">
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
            {/* Create dropdown */}
            <div ref={createRef} className="relative">
              <NavBtn
                active={createOpen}
                onClick={() => setCreateOpen(o => !o)}
                icon={<Plus className="h-4 w-4" />}
                hasChevron
              >
                Create
              </NavBtn>
              {createOpen && (
                <div className="absolute left-0 top-full z-40 mt-1 w-60 overflow-hidden rounded-lg border border-rule bg-white py-1 text-ink shadow-xl">
                  {FORM_GROUPS.map(g => (
                    <div key={g.group}>
                      <div className="bg-slate-50 px-3 py-1.5 text-xs font-semibold uppercase tracking-wider text-slate-400">
                        {g.group}
                      </div>
                      {g.items.map(item => (
                        <button
                          key={item.id}
                          onClick={() => { setScreen({ kind: 'form', code: item.id }); setCreateOpen(false) }}
                          className="block w-full px-4 py-2 text-left text-sm text-ink-muted transition-colors hover:bg-blue-50 hover:text-blue-700"
                        >
                          <span className="font-mono text-[10.5px] text-ink-faint mr-2">{item.id}</span>
                          {item.label}
                          {item.id === 'LEAVE' && <span className="ml-2 inline-block rounded bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold text-amber-700">NEW</span>}
                        </button>
                      ))}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <NavBtn active={screen.kind === 'home'} onClick={() => setScreen({ kind: 'home' })} icon={<Home className="h-4 w-4" />}>Home</NavBtn>
            <NavBtn active={screen.kind === 'search'} onClick={() => setScreen({ kind: 'search' })} icon={<Search className="h-4 w-4" />}>Search</NavBtn>
            <NavBtn icon={<FileText className="h-4 w-4" />} onClick={() => alert('User Guide — placeholder')}>User Guide</NavBtn>
            <NavBtn active={screen.kind === 'report'} onClick={() => setScreen({ kind: 'report' })} icon={<BarChart3 className="h-4 w-4" />}>Report</NavBtn>
            <NavBtn active={screen.kind === 'onboarding'} onClick={() => setScreen({ kind: 'onboarding' })} icon={<Sparkles className="h-4 w-4" />}>Onboard</NavBtn>
          </div>

          {/* Right side */}
          <div className="ml-auto flex items-center gap-1">
            <button title="Notifications" className="flex h-8 w-8 items-center justify-center rounded text-white/70 transition-colors hover:bg-white/10 hover:text-white">
              <Bell className="h-4 w-4" />
            </button>
            <button title="Help" className="flex h-8 w-8 items-center justify-center rounded text-white/70 transition-colors hover:bg-white/10 hover:text-white">
              <HelpCircle className="h-4 w-4" />
            </button>
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
