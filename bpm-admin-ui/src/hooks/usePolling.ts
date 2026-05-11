import { useEffect, useRef } from 'react'

/**
 * PR-K4 §5.3 — visibility-aware polling. Calls <code>fn</code> immediately
 * on mount, then every <code>intervalMs</code> milliseconds. Pauses when
 * <code>document.visibilityState === 'hidden'</code> so background tabs
 * don't keep hitting the LiveCases endpoints (which would otherwise keep
 * a stale polling timer alive across hours of being backgrounded).
 *
 * The polled callback is captured in a ref so callers can pass an inline
 * arrow function without resetting the timer on every render.
 */
export function usePolling(fn: () => void | Promise<void>, intervalMs: number) {
  const fnRef = useRef(fn)
  fnRef.current = fn

  useEffect(() => {
    let cancelled = false
    let timer: number | null = null

    const tick = async () => {
      if (cancelled) return
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') return
      try { await fnRef.current() } catch { /* swallow — caller surfaces errors via state */ }
    }

    const start = () => {
      if (timer != null) return
      // Fire immediately so a foreground re-show repaints with fresh data.
      void tick()
      timer = window.setInterval(() => { void tick() }, intervalMs)
    }

    const stop = () => {
      if (timer != null) {
        window.clearInterval(timer)
        timer = null
      }
    }

    const onVisibility = () => {
      if (document.visibilityState === 'visible') start()
      else stop()
    }

    start()
    document.addEventListener('visibilitychange', onVisibility)

    return () => {
      cancelled = true
      stop()
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [intervalMs])
}
