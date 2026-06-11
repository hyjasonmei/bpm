import * as React from 'react'
import { Plus, Search, Home as HomeIcon, ChevronDown, Clock } from 'lucide-react'
import { Link, NavLink, useMatch } from 'react-router-dom'

import { cn } from '@/lib/cn'
import { AccountMenu } from '@/components/AccountMenu'
import { DelegationButton } from '@/components/DelegationButton'
import { ImpersonationBanner } from '@/components/ImpersonationBanner'
import { SandboxBanner } from '@/components/SandboxBanner'
import { NotificationsMenu } from '@/components/NotificationsMenu'
import { HelpReportMenu } from '@/components/HelpReportMenu'
import type { PersonaCode } from '@/lib/role'
import { FORMS, type FormCode } from '@/lib/workflow'
import { useBranding } from '@/lib/branding'

interface AppLayoutProps {
  persona: PersonaCode
  setPersona: (p: PersonaCode) => void | Promise<void>
  authedFullName?: string | null
  authPending?: boolean
  authError?: string | null
  children: React.ReactNode
}

export function AppLayout({ persona, setPersona, authedFullName = null, authPending = false, authError = null, children }: AppLayoutProps) {
  // Form sub-header is shown when the URL matches /apply/:code or /tasks/:taskId.
  // For /tasks/:taskId we don't know the FormCode synchronously — the sub-header
  // just shows when on an apply path; task pages skip it (the task panel shows
  // its own context inside the form body).
  const applyMatch = useMatch('/apply/:code')

  const branding = useBranding()

  return (
    <div className="min-h-screen bg-bg">
      <SandboxBanner />
      <ImpersonationBanner />
      {/* ── Header ────────────────────────────────────────── */}
      <header className="no-print sticky top-0 z-40 bg-header text-white shadow-md">
        <div className="mx-auto flex h-12 max-w-screen-2xl items-center gap-2 px-4">
          {/* Logo */}
          <Link to="/" className="mr-4 flex items-center gap-2 transition-opacity hover:opacity-90">
            {branding.logoDataUri ? (
              <img src={branding.logoDataUri} alt="" className="h-7 w-auto max-w-[120px] object-contain" />
            ) : (
              <div className="flex h-7 w-7 items-center justify-center rounded bg-red-500 text-[10.5px] font-bold tracking-wider text-white">BPM</div>
            )}
            <span className="text-sm font-bold tracking-wide">{branding.systemName ?? 'BPM System'}</span>
          </Link>

          {/* Nav */}
          <div className="flex items-center gap-0.5">
            <NavBtnLink to="/" end icon={<HomeIcon className="h-4 w-4" />}>Home</NavBtnLink>
            <NavBtnLink to="/create" icon={<Plus className="h-4 w-4" />}>Create</NavBtnLink>
            <NavBtnLink to="/search" icon={<Search className="h-4 w-4" />}>Search</NavBtnLink>
            <NavBtnLink to="/attendance" icon={<Clock className="h-4 w-4" />}>Attendance</NavBtnLink>
          </div>

          {/* Right side */}
          <div className="ml-auto flex items-center gap-1">
            <NotificationsMenu />
            <HelpReportMenu />
            <DelegationButton />
            <AccountMenu
              active={persona}
              onChange={setPersona}
              pending={authPending}
              error={authError}
              authedFullName={authedFullName}
            />
          </div>
        </div>

        {/* Stepper bar slot — forms render here via children + portal-like gap */}
        {applyMatch?.params.code && (
          <div className="border-t border-white/10 bg-white/5 px-4">
            <div className="mx-auto max-w-screen-2xl">
              <FormSubHeader code={applyMatch.params.code as FormCode} />
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

function NavBtnLink({ to, end, icon, children, hasChevron }: {
  to: string
  end?: boolean
  icon?: React.ReactNode
  children: React.ReactNode
  hasChevron?: boolean
}) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) => cn(
        'flex items-center gap-1.5 rounded px-3 py-1.5 text-sm font-medium transition-colors',
        isActive ? 'bg-white/20 text-white' : 'text-white/80 hover:bg-white/10 hover:text-white',
      )}
    >
      {icon}
      {children}
      {hasChevron && <ChevronDown className="h-3.5 w-3.5 opacity-70" />}
    </NavLink>
  )
}

function FormSubHeader({ code }: { code: FormCode }) {
  // Stepper is rendered inside the form screen itself.
  // This sub-header just shows a thin breadcrumb for context.
  const def = FORMS[code]
  if (!def) return null
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
