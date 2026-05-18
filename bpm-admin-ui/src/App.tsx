import { useEffect, useState } from 'react'
import { AdminLayout, type AdminScreen } from '@/components/AdminLayout'
import { NoPermission } from '@/components/NoPermission'
import { apiFetch, getJwt, setJwt } from '@/lib/apiFetch'
import { decodeJwt, isAdmin } from '@/lib/jwt'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import { ProcessAdminShell } from '@/screens/processes/ProcessAdminShell'
import { FlowLibrary } from '@/screens/FlowLibrary/FlowLibrary'
import { SiteSettings } from '@/screens/SiteSettings'
import { UsersRoles } from '@/screens/UsersRoles'
import { Impersonation } from '@/screens/Impersonation'
import { AuditLogs } from '@/screens/AuditLogs'
import { SandboxMailbox } from '@/screens/sandbox/SandboxMailbox'
import { FlowcookRoot } from '@/flowcook/Root'

const SCREEN_KEY = 'bpm_admin_screen'
const LEGACY_FLAG_KEY = 'flowcook_legacy_visible'
type GateState = 'pending' | 'authorized' | 'forbidden'

function readSavedScreen(): AdminScreen {
  try {
    const raw = localStorage.getItem(SCREEN_KEY)
    if (!raw) return { kind: 'onboarding' }
    const p = JSON.parse(raw)
    if (p && typeof p.kind === 'string') return p as AdminScreen
  } catch { /* ignore */ }
  return { kind: 'onboarding' }
}

function readLegacyFlag(): boolean {
  try {
    return localStorage.getItem(LEGACY_FLAG_KEY) === '1'
  } catch {
    return false
  }
}

function setLegacyFlag(v: boolean) {
  try {
    if (v) localStorage.setItem(LEGACY_FLAG_KEY, '1')
    else localStorage.removeItem(LEGACY_FLAG_KEY)
  } catch {
    /* ignore */
  }
}

export default function App() {
  // flowcook is the new five-page shell. Legacy admin (Onboarding /
  // ProcessAdminShell / FlowLibrary / etc.) only appears when the user
  // explicitly enables the legacy flag.
  const [showLegacy, setShowLegacy] = useState<boolean>(readLegacyFlag)

  if (!showLegacy) {
    return (
      <FlowcookRoot
        onShowLegacy={() => {
          setLegacyFlag(true)
          setShowLegacy(true)
        }}
      />
    )
  }

  return (
    <LegacyApp
      onExitLegacy={() => {
        setLegacyFlag(false)
        setShowLegacy(false)
      }}
    />
  )
}

function LegacyApp({ onExitLegacy }: { onExitLegacy: () => void }) {
  const [gate, setGate] = useState<GateState>('pending')
  const [screen, setScreen] = useState<AdminScreen>(readSavedScreen)

  useEffect(() => {
    localStorage.setItem(SCREEN_KEY, JSON.stringify(screen))
  }, [screen])

  // Admin role guard. In dev mode, if no JWT, auto-mint as admin persona.
  useEffect(() => {
    let cancelled = false

    async function ensureAuth() {
      let token = getJwt()
      if (!token) {
        try {
          const res = await apiFetch('/api/dev/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ PersonaCode: 'admin' }),
          })
          if (res.ok) {
            const data = await res.json()
            setJwt(data.token)
            token = data.token
          }
        } catch {
          /* fall through; will end up forbidden */
        }
      }
      if (cancelled) return
      const decoded = token ? decodeJwt(token) : null
      setGate(isAdmin(decoded) ? 'authorized' : 'forbidden')
    }

    void ensureAuth()
    return () => { cancelled = true }
  }, [])

  if (gate === 'pending') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-bg text-sm text-ink-muted">
        Loading…
      </div>
    )
  }

  if (gate === 'forbidden') return <NoPermission />

  let body: React.ReactNode
  switch (screen.kind) {
    case 'onboarding':    body = <Onboarding onNavigate={setScreen} />; break
    case 'processes':     body = <ProcessAdminShell />; break
    case 'flow-library':  body = <FlowLibrary />; break
    case 'site-settings':    body = <SiteSettings />; break
    case 'sandbox-mailbox':  body = <SandboxMailbox />; break
    case 'users-roles':      body = <UsersRoles />; break
    case 'impersonation':    body = <Impersonation />; break
    case 'audit':            body = <AuditLogs />; break
  }

  return (
    <AdminLayout screen={screen} setScreen={setScreen}>
      <div className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
        ⚠ Legacy admin UI. The new flowcook shell is the default — these
        pages are scheduled to be migrated and retired across Steps 3-5.
        <button onClick={onExitLegacy} className="ml-3 underline">
          Back to flowcook
        </button>
      </div>
      {body}
    </AdminLayout>
  )
}
