import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { setJwt } from './lib/apiFetch'

// Sandbox persona hand-off: the admin Sandbox console opens bpm-ui with
// ?sandboxToken=<jwt> after minting a persona token. Adopt it as the session
// token and strip the param before React boots, so the AuthGate sees the
// persona logged in and the token never lingers in the address bar / history.
{
  const params = new URLSearchParams(window.location.search)
  const sandboxToken = params.get('sandboxToken')
  if (sandboxToken) {
    setJwt(sandboxToken)
    params.delete('sandboxToken')
    const qs = params.toString()
    window.history.replaceState({}, '', window.location.pathname + (qs ? `?${qs}` : '') + window.location.hash)
  }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
