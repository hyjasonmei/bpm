import { useEffect, useState } from 'react'
import { getUnreadCount, getSandboxStatus } from '@/lib/api/sandbox'
import type { UnreadCountDto } from '@/types/sandbox'

const ZERO: UnreadCountDto = { total: 0, byChannel: {} }

interface State {
  count: UnreadCountDto
  sandboxOn: boolean
}

/**
 * PR-J5 §9.7: nav-badge poller. Every 10s, refreshes
 * `/api/sandbox/captured/unread-count` IF sandbox is on.
 * - Sandbox-off branches return zero counts WITHOUT a DB hit on the server,
 *   but we still skip the request to keep the network tab quiet in prod.
 * - The sandbox-on flag itself is also polled (cheap status endpoint) so the
 *   badge auto-disappears when an admin flips sandbox off in another tab.
 */
export function useSandboxUnreadCount(intervalMs = 10_000): State {
  const [state, setState] = useState<State>({ count: ZERO, sandboxOn: false })

  useEffect(() => {
    let cancelled = false

    async function tick() {
      try {
        const status = await getSandboxStatus()
        if (cancelled) return
        if (!status.enabled) {
          setState({ count: ZERO, sandboxOn: false })
          return
        }
        const c = await getUnreadCount()
        if (cancelled) return
        setState({ count: c, sandboxOn: true })
      } catch {
        // swallow — banner stays in last-known state on transient failures
      }
    }

    void tick()
    const handle = window.setInterval(tick, intervalMs)
    return () => { cancelled = true; window.clearInterval(handle) }
  }, [intervalMs])

  return state
}
