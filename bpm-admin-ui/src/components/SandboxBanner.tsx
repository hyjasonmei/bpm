import { useEffect, useState } from 'react'
import { TestTube } from 'lucide-react'
import { getSandboxStatus, getUnreadCount, getClock } from '@/lib/api/sandbox'
import type { UnreadCountDto, SandboxClockDto } from '@/types/sandbox'

interface BannerState {
  enabled: boolean
  unread: UnreadCountDto | null
  clock: SandboxClockDto | null
}

const POLL_MS = 10_000

/**
 * PR-J5 §9.8: live banner. When sandbox is on, polls captured + clock
 * every 10s and surfaces the totals so testers always know the platform
 * has rerouted their outbound traffic and how many days into the future
 * the clock has been wound.
 */
export function SandboxBanner() {
  const [state, setState] = useState<BannerState>({ enabled: false, unread: null, clock: null })

  useEffect(() => {
    let cancelled = false

    async function tick() {
      try {
        const status = await getSandboxStatus()
        if (cancelled) return
        if (!status.enabled) {
          setState({ enabled: false, unread: null, clock: null })
          return
        }
        const [unread, clock] = await Promise.all([
          getUnreadCount().catch(() => null),
          getClock().catch(() => null),
        ])
        if (cancelled) return
        setState({ enabled: true, unread, clock })
      } catch {
        // swallow — keep last-good state on transient failures
      }
    }

    void tick()
    const handle = window.setInterval(tick, POLL_MS)
    return () => { cancelled = true; window.clearInterval(handle) }
  }, [])

  if (!state.enabled) return null

  const mail    = state.unread?.byChannel?.['Email']   ?? state.unread?.byChannel?.['1'] ?? 0
  const webhook = state.unread?.byChannel?.['Webhook'] ?? state.unread?.byChannel?.['2'] ?? 0
  const offset  = state.clock ? formatOffset(state.clock.offsetSeconds) : '+0'

  return (
    <div className="flex items-center justify-center gap-2 bg-amber-500 px-4 py-1.5 text-xs font-semibold text-white shadow">
      <TestTube className="h-3.5 w-3.5" />
      <span>SANDBOX MODE ACTIVE — captured: {mail} mail / {webhook} webhook · clock {offset}</span>
    </div>
  )
}

function formatOffset(seconds: number): string {
  if (seconds === 0) return '+0'
  const sign = seconds >= 0 ? '+' : '-'
  const abs = Math.abs(seconds)
  const days  = Math.floor(abs / 86_400)
  const hours = Math.floor((abs % 86_400) / 3_600)
  const mins  = Math.floor((abs % 3_600) / 60)
  const parts: string[] = []
  if (days)  parts.push(`${days}d`)
  if (hours) parts.push(`${hours}h`)
  if (!days && !hours && mins) parts.push(`${mins}m`)
  if (!parts.length) parts.push('<1m')
  return sign + parts.join(' ')
}
