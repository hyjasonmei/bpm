import { useEffect, useState } from 'react'
import { AlertTriangle, LogOut } from 'lucide-react'
import { cn } from '@/lib/cn'
import { decodeJwt } from '@/lib/jwt'
import { getJwt } from '@/lib/apiFetch'
import { exitImpersonationLocal, isImpersonating } from '@/lib/impersonationToken'
import { endImpersonation } from '@/lib/api/impersonation'

export function ImpersonationBanner() {
  const [tick, setTick] = useState(0)
  useEffect(() => {
    const t = setInterval(() => setTick(x => x + 1), 1000)
    return () => clearInterval(t)
  }, [])
  // also listen for swap-back events from apiFetch 401 handler
  useEffect(() => {
    const onEnded = () => setTick(x => x + 1)
    window.addEventListener('bpm:impersonation-ended', onEnded)
    return () => window.removeEventListener('bpm:impersonation-ended', onEnded)
  }, [])

  if (!isImpersonating()) return null

  const jwt = getJwt()!
  const decoded = decodeJwt(jwt)
  const targetName = decoded?.full_name ?? decoded?.email ?? 'unknown user'
  const exp = decoded?.exp ? decoded.exp * 1000 : null
  const remainingMs = exp ? Math.max(0, exp - Date.now()) : 0
  const mm = Math.floor(remainingMs / 60000)
  const ss = Math.floor((remainingMs % 60000) / 1000)
  const tone =
    mm < 1 ? 'bg-red-700 text-white animate-pulse' :
    mm < 5 ? 'bg-amber-600 text-white' :
    'bg-red-600 text-white'

  // suppress unused tick warning
  void tick

  async function exit() {
    try { await endImpersonation() } catch { /* swallow */ }
    exitImpersonationLocal()
    window.location.reload()
  }

  return (
    <div className={cn('flex items-center justify-between gap-3 px-4 py-2 text-xs font-medium shadow', tone)}>
      <div className="flex items-center gap-2">
        <AlertTriangle className="h-4 w-4" />
        <span>
          ⚠️ ACTING AS <strong>{targetName}</strong>
          {' · '}
          <span className="font-mono tabular">{String(mm).padStart(2, '0')}:{String(ss).padStart(2, '0')} left</span>
        </span>
      </div>
      <button
        onClick={exit}
        className="inline-flex items-center gap-1 rounded bg-white/20 px-2 py-1 text-xs font-semibold text-white hover:bg-white/30"
      >
        <LogOut className="h-3.5 w-3.5" />
        Exit
      </button>
    </div>
  )
}
