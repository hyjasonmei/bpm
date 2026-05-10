import { useEffect, useState } from 'react'
import { ShieldAlert } from 'lucide-react'

const REDIRECT_AFTER_SEC = 3

export function NoPermission() {
  const [secondsLeft, setSecondsLeft] = useState(REDIRECT_AFTER_SEC)

  useEffect(() => {
    const t = setInterval(() => {
      setSecondsLeft(prev => {
        if (prev <= 1) {
          window.location.replace('/app/')
          return 0
        }
        return prev - 1
      })
    }, 1000)
    return () => clearInterval(t)
  }, [])

  return (
    <div className="flex min-h-screen items-center justify-center bg-bg px-4">
      <div className="max-w-md rounded-lg border border-rule bg-card p-8 text-center shadow-sm">
        <ShieldAlert className="mx-auto h-12 w-12 text-amber-500" />
        <h1 className="mt-4 text-xl font-bold text-ink">No permission</h1>
        <p className="mt-2 text-sm text-ink-muted">
          The Admin Console is restricted to users with the <code className="rounded bg-slate-100 px-1 font-mono">admin</code> role.
        </p>
        <p className="mt-4 text-sm text-ink-muted">
          Redirecting to the Employee app in <span className="font-bold tabular text-primary">{secondsLeft}</span> second{secondsLeft !== 1 ? 's' : ''}…
        </p>
        <a href="/app/" className="mt-6 inline-block text-sm text-primary underline">Go now</a>
      </div>
    </div>
  )
}
