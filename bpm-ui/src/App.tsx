import { useEffect, useState } from 'react'
import { AppLayout, type Screen } from '@/components/AppLayout'
import { useActivePersona } from '@/lib/role'
import { Home } from '@/screens/Home'
import { CreateIndex } from '@/screens/CreateIndex'
import { Search } from '@/screens/Search'
import { Report } from '@/screens/Report'
import { Attendance } from '@/screens/Attendance'
import { LeaveForm } from '@/screens/forms/Reference_LeaveForm'
import { GEEForm } from '@/screens/forms/Reference_GEEForm'
import { GEVForm } from '@/screens/forms/Reference_GEVForm'
import { APEForm } from '@/screens/forms/Reference_APEForm'
import { HWPForm } from '@/screens/forms/Reference_HWPForm'
import { TRQView } from '@/screens/forms/Reference_TRQView'
import { TEOView } from '@/screens/forms/Reference_TEOView'
import { ITPRView } from '@/screens/forms/Reference_ITPRView'
import { EXTOBView } from '@/screens/forms/Reference_EXTOBView'
import { ResignForm } from '@/screens/forms/Reference_ResignForm'
import { DeptxForm } from '@/screens/forms/Reference_DeptxForm'
import { NotCookedYet } from '@/screens/forms/NotCookedYet'
import { SandboxMailbox } from '@/screens/SandboxMailbox'
import { lookupForm } from '@/features/registry'

// Hand-coded forms were renamed Reference_* to mark them as the
// reference set chef reads when generating new flows; their UI is
// hidden by default. Flip VITE_SHOW_LEGACY=true to expose them for
// local development / regression checks.
const SHOW_LEGACY_FORMS = import.meta.env.VITE_SHOW_LEGACY === 'true'

const SCREEN_KEY = 'bpm_screen'

function readSavedScreen(): Screen {
  try {
    const raw = localStorage.getItem(SCREEN_KEY)
    if (!raw) return { kind: 'home' }
    const parsed = JSON.parse(raw)
    // Stale onboarding screen from before admin-ui-split moved it out
    if (parsed?.kind === 'onboarding') return { kind: 'home' }
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
    case 'create':     body = <CreateIndex setScreen={setScreen} />; break
    case 'search':     body = <Search />; break
    case 'report':     body = <Report />; break
    case 'attendance': body = <Attendance />; break
    case 'sandbox-mailbox': body = <SandboxMailbox />; break
    case 'form': {
      const formMode = screen.taskId ? 'task' : 'create'
      const onSubmitted = () => setScreen({ kind: 'home' })

      // Primary path: any chef-shipped flow has a manifest under
      // features/<CODE>/V<N>/ — registry resolves to the highest
      // version automatically. No central switch to edit when a new
      // flow lands.
      const manifest = lookupForm(screen.code)
      if (manifest) {
        const Form = manifest.component
        body = <Form persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />
        break
      }

      // Legacy fallback: opt in with VITE_SHOW_LEGACY=true to render
      // the Reference_* hand-coded components instead of the chef
      // "not cooked yet" placeholder. The Reference_* set isn't
      // re-exported from a manifest because chef regenerates each
      // flow from scratch, so the legacy and chef paths share no
      // state.
      if (SHOW_LEGACY_FORMS) {
        switch (screen.code) {
          case 'LEAVE': body = <LeaveForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'GEE':   body = <GEEForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'GEV':   body = <GEVForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'APE':   body = <APEForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'HWP':   body = <HWPForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'TRQ':   body = <TRQView persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'TEO':   body = <TEOView persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'ITPR':  body = <ITPRView persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'EXTOB': body = <EXTOBView persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'RESIGN': body = <ResignForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
          case 'DEPTX': body = <DeptxForm persona={persona} mode={formMode} taskId={screen.taskId ?? null} onSubmitted={onSubmitted} />; break
        }
        break
      }

      body = <NotCookedYet code={screen.code} onHome={() => setScreen({ kind: 'home' })} />
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
