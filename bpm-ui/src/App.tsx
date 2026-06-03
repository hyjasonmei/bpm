import { useEffect, useState } from 'react'
import { RouterProvider } from 'react-router-dom'

import { router } from '@/router'
import { getJwt } from '@/lib/apiFetch'
import { Login } from '@/screens/Login'
import { applyBrandingToDocument, useBranding } from '@/lib/branding'

export default function App() {
  // White-label: apply tenant tab title + favicon as soon as branding loads.
  const branding = useBranding()
  useEffect(() => { applyBrandingToDocument(branding) }, [branding])

  // AuthGate: until a JWT is present, render the Login screen. The router
  // takes over once we're authenticated. Login fires `bpm:auth-cleared` /
  // reload on success so all hooks (incl. useActivePersona) rerun under
  // the new identity.
  const [hasJwt, setHasJwt] = useState(() => getJwt() != null)
  useEffect(() => {
    const onCleared = () => setHasJwt(false)
    window.addEventListener('bpm:auth-cleared', onCleared as EventListener)
    return () => window.removeEventListener('bpm:auth-cleared', onCleared as EventListener)
  }, [])

  if (!hasJwt) {
    return <Login onLoggedIn={() => { setHasJwt(true); window.location.reload() }} />
  }

  return <RouterProvider router={router} />
}
