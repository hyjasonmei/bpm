import { useCallback, useEffect, useRef, useState } from 'react'
import { myTasks } from '@/lib/api/process'
import type { ProcessTaskDto } from '@/types/process'

const POLL_INTERVAL_MS = 30_000

export interface UseMyTasksResult {
  data: ProcessTaskDto[] | null
  loading: boolean
  error: Error | null
  /** Force an immediate refresh outside the polling cadence. */
  refresh: () => Promise<void>
}

/**
 * Subscribes the calling component to /api/tasks/mine, refreshing every 30s.
 *
 * Returns the latest task list, a loading flag (true only on the very first
 * fetch — subsequent polls keep the previous data while in-flight, to avoid
 * UI flicker), the last error if any, and a `refresh()` for explicit pulls
 * (e.g. immediately after a submit/return action).
 *
 * The hook tolerates the filter changing — switching from 'open' to
 * 'completed' resets data to null and re-fetches. Polling continues against
 * the most recently selected filter.
 */
export function useMyTasks(filter: 'open' | 'completed' | 'all' = 'open'): UseMyTasksResult {
  const [data, setData] = useState<ProcessTaskDto[] | null>(null)
  const [loading, setLoading] = useState<boolean>(true)
  const [error, setError] = useState<Error | null>(null)

  // Cancellation guard: ignore stale fetches after unmount or filter switch.
  const cancelledRef = useRef(false)

  const fetchOnce = useCallback(async (signalCancelled: () => boolean) => {
    try {
      const next = await myTasks(filter)
      if (signalCancelled()) return
      setData(next)
      setError(null)
    } catch (err) {
      if (signalCancelled()) return
      setError(err instanceof Error ? err : new Error(String(err)))
    } finally {
      if (!signalCancelled()) setLoading(false)
    }
  }, [filter])

  const refresh = useCallback(async () => {
    await fetchOnce(() => cancelledRef.current)
  }, [fetchOnce])

  useEffect(() => {
    cancelledRef.current = false
    setData(null)
    setLoading(true)
    setError(null)

    const isCancelled = () => cancelledRef.current
    void fetchOnce(isCancelled)
    const handle = window.setInterval(() => {
      void fetchOnce(isCancelled)
    }, POLL_INTERVAL_MS)

    // Listen for the runtime invalidation event so Home picks up the new
    // task within ~ms of a submit, instead of waiting up to 30 s. See
    // openspec change `redirect-home-after-submit`.
    const onInvalidate = () => { void fetchOnce(isCancelled) }
    window.addEventListener('bpm:tasks-invalidate', onInvalidate)

    return () => {
      cancelledRef.current = true
      window.clearInterval(handle)
      window.removeEventListener('bpm:tasks-invalidate', onInvalidate)
    }
  }, [fetchOnce])

  return { data, loading, error, refresh }
}
