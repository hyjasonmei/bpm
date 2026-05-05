import { useEffect, useState } from 'react'
import { AppLayout, type Screen } from '@/components/AppLayout'
import { useActivePersona } from '@/lib/role'
import { Home } from '@/screens/Home'
import { Search } from '@/screens/Search'
import { Report } from '@/screens/Report'
import { Onboarding } from '@/screens/onboarding/Onboarding'
import { LeaveForm } from '@/screens/forms/LeaveForm'
import { GEEForm } from '@/screens/forms/GEEForm'
import { GEVForm } from '@/screens/forms/GEVForm'
import { APEForm } from '@/screens/forms/APEForm'
import { HWPForm } from '@/screens/forms/HWPForm'
import { TRQView } from '@/screens/forms/TRQView'
import { TEOView } from '@/screens/forms/TEOView'
import { ITPRView } from '@/screens/forms/ITPRView'
import { EXTOBView } from '@/screens/forms/EXTOBView'

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
  const { code: persona, setCode: setPersona, authedUser, pending: authPending, error: authError } = useActivePersona()
  const [screen, setScreen] = useState<Screen>(readSavedScreen)

  useEffect(() => {
    localStorage.setItem(SCREEN_KEY, JSON.stringify(screen))
  }, [screen])

  let body: React.ReactNode
  switch (screen.kind) {
    case 'home':       body = <Home persona={persona} setScreen={setScreen} />; break
    case 'search':     body = <Search />; break
    case 'report':     body = <Report />; break
    case 'onboarding': body = <Onboarding />; break
    case 'form':
      switch (screen.code) {
        case 'LEAVE': body = <LeaveForm persona={persona} />; break
        case 'GEE':   body = <GEEForm persona={persona} />; break
        case 'GEV':   body = <GEVForm persona={persona} />; break
        case 'APE':   body = <APEForm persona={persona} />; break
        case 'HWP':   body = <HWPForm persona={persona} />; break
        case 'TRQ':   body = <TRQView persona={persona} />; break
        case 'TEO':   body = <TEOView persona={persona} />; break
        case 'ITPR':  body = <ITPRView persona={persona} />; break
        case 'EXTOB': body = <EXTOBView persona={persona} />; break
      }
      break
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
