import { useEffect, useState } from 'react'
import { AppLayout, type Screen } from '@/components/AppLayout'
import { useActivePersona } from '@/lib/role'
import { Home } from '@/screens/Home'
import { Search } from '@/screens/Search'
import { Report } from '@/screens/Report'
import { LeaveForm } from '@/screens/forms/LeaveForm'
import { GEEForm } from '@/screens/forms/GEEForm'
import { PlaceholderForm } from '@/screens/forms/PlaceholderForm'

const SCREEN_KEY = 'bpm_screen'

function readSavedScreen(): Screen {
  try {
    const raw = localStorage.getItem(SCREEN_KEY)
    if (!raw) return { kind: 'home' }
    const parsed = JSON.parse(raw)
    if (parsed && typeof parsed.kind === 'string') return parsed as Screen
  } catch { /* ignore */ }
  return { kind: 'home' }
}

export default function App() {
  const { code: persona, setCode: setPersona } = useActivePersona()
  const [screen, setScreen] = useState<Screen>(readSavedScreen)

  useEffect(() => {
    localStorage.setItem(SCREEN_KEY, JSON.stringify(screen))
  }, [screen])

  let body: React.ReactNode
  switch (screen.kind) {
    case 'home':   body = <Home persona={persona} setScreen={setScreen} />; break
    case 'search': body = <Search />; break
    case 'report': body = <Report />; break
    case 'form':
      switch (screen.code) {
        case 'LEAVE': body = <LeaveForm persona={persona} />; break
        case 'GEE':   body = <GEEForm persona={persona} />; break
        default:      body = <PlaceholderForm code={screen.code} persona={persona} />; break
      }
      break
  }

  return (
    <AppLayout screen={screen} setScreen={setScreen} persona={persona} setPersona={setPersona}>
      {body}
    </AppLayout>
  )
}
