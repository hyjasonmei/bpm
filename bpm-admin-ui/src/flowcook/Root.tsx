import { BrowserRouter } from 'react-router-dom'
import { AppShell } from '@/flowcook/app/AppShell'
import { LoginPage } from '@/flowcook/auth/LoginPage'
import { AuthProvider, useAuth } from '@/flowcook/auth/useAuth'

interface RootProps {
  onShowLegacy?: () => void
}

function Inner({ onShowLegacy }: RootProps) {
  const { status } = useAuth()
  if (status === 'pending') {
    return (
      <div className="flex min-h-screen items-center justify-center bg-bg text-sm text-ink-muted">
        Loading…
      </div>
    )
  }
  if (status === 'unauthenticated') return <LoginPage />
  return (
    <BrowserRouter>
      <AppShell onShowLegacy={onShowLegacy} />
    </BrowserRouter>
  )
}

export function FlowcookRoot(props: RootProps) {
  return (
    <AuthProvider>
      <Inner {...props} />
    </AuthProvider>
  )
}
