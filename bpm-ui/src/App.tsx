import { useEffect, useState } from 'react'
import { AppLayout, type Screen } from '@/components/AppLayout'
import { useActivePersona } from '@/lib/role'
import { getJwt } from '@/lib/apiFetch'
import { Home } from '@/screens/Home'
import { CreateIndex } from '@/screens/CreateIndex'
import { Search } from '@/screens/Search'
import { Report } from '@/screens/Report'
import { Attendance } from '@/screens/Attendance'
import { Login } from '@/screens/Login'
import { NotCookedYet } from '@/screens/forms/NotCookedYet'
import { SandboxMailbox } from '@/screens/SandboxMailbox'
import { lookupForm } from '@/features/registry'

const SCREEN_KEY = 'bpm_screen'

function readSavedScreen(): Screen {
  try {
    const raw = localStorage.getItem(SCREEN_KEY)
    if (!raw) return { kind: 'home' }
    const parsed = JSON.parse(raw)
    if (parsed?.kind === 'onboarding') return { kind: 'home' }
    if (parsed && typeof parsed.kind === 'string') return parsed as Screen
  } catch { /* ignore */ }
  return { kind: 'home' }
}

export default function App() {
  // AuthGate: until a JWT is present, render the Login screen. The Login
  // component reloads after success so all hooks (incl. useActivePersona)
  // rerun under the new identity.
  const [hasJwt, setHasJwt] = useState(() => getJwt() != null)
  useEffect(() => {
    const onCleared = () => setHasJwt(false)
    window.addEventListener('bpm:auth-cleared', onCleared as EventListener)
    return () => window.removeEventListener('bpm:auth-cleared', onCleared as EventListener)
  }, [])

  if (!hasJwt) {
    return <Login onLoggedIn={() => { setHasJwt(true); window.location.reload() }} />
  }

  return <AppShell />
}

function AppShell() {
  const { code: persona, setCode: setPersona, authedUser, pending: authPending, error: authError } = useActivePersona()
  const [screen, setScreen] = useState<Screen>(readSavedScreen)

  useEffect(() => {
    localStorage.setItem(SCREEN_KEY, JSON.stringify(screen))
  }, [screen])

  let body: React.ReactNode
  switch (screen.kind) {
    case 'home':       body = <Home persona={persona} setScreen={setScreen} />; break
    case 'create':     body = <CreateIndex setScreen={setScreen} />; break
    case 'search':     body = <Search />; break
    case 'report':     body = <Report />; break
    case 'attendance': body = <Attendance />; break
    case 'sandbox-mailbox': body = <SandboxMailbox />; break
    case 'form': {
      const formMode = screen.taskId ? 'task' : 'create'
      const onSubmitted = () => setScreen({ kind: 'home' })
      const manifest = lookupForm(screen.code)
      if (manifest) {
        const Form = manifest.component
        body = <Form persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />
      } else {
        body = <NotCookedYet code={screen.code} onHome={() => setScreen({ kind: 'home' })} />
      }
      break
    }
  }

  return (
    <AppLayout
      screen={screen}
      setScreen={setScreen}
      persona={persona}
      setPersona={setPersona}
      authedFullName={authedUser?.fullName ?? null}
      authPending={authPending}
      authError={authError}
    >
      {body}
    </AppLayout>
  )
}
