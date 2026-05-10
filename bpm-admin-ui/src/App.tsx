import { useEffect, useState } from 'react'
import { AdminLayout, type AdminScreen } from '@/components/AdminLayout'
import { NoPermission } from '@/components/NoPermission'
import { apiFetch, getJwt, setJwt } from '@/lib/apiFetch'
import { decodeJwt, isAdmin } from '@/lib/jwt'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import { SiteSettings } from '@/screens/SiteSettings'
import { UsersRoles } from '@/screens/UsersRoles'
import { Impersonation } from '@/screens/Impersonation'
import { AuditLogs } from '@/screens/AuditLogs'

const SCREEN_KEY = 'bpm_admin_screen'
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

export default function App() {
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
    case 'onboarding':    body = <Onboarding />; break
    case 'site-settings': body = <SiteSettings />; break
    case 'users-roles':   body = <UsersRoles />; break
    case 'impersonation': body = <Impersonation />; break
    case 'audit':         body = <AuditLogs />; break
  }

  return (
    <AdminLayout screen={screen} setScreen={setScreen}>
      {body}
    </AdminLayout>
  )
}
