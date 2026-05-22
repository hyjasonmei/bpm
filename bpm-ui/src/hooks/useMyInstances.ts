import { useCallback, useEffect, useRef, useState } from 'react'
import { myInstances } from '@/lib/api/process'
import type { MyInstanceSummaryDto } from '@/types/process'

const POLL_INTERVAL_MS = 30_000

export interface UseMyInstancesResult {
  data: MyInstanceSummaryDto[] | null
  loading: boolean
  error: Error | null
  /** Force an immediate refresh outside the polling cadence. */
  refresh: () => Promise<void>
}

/**
 * Subscribes the calling component to /api/processes/mine, refreshing every
 * 30s. Mirrors {@link useMyTasks}'s lifecycle — first-fetch loading flag,
 * stale-fetch cancellation guard, and a manual `refresh()` for use after
 * mutations (start a process / submit a task).
 *
 * Switching `filter` resets to a clean loading state (data → null) and
 * polls against the new filter.
 */
export function useMyInstances(filter: 'active' | 'completed' | 'all' = 'all'): UseMyInstancesResult {
  const [data, setData] = useState<MyInstanceSummaryDto[] | null>(null)
  const [loading, setLoading] = useState<boolean>(true)
  const [error, setError] = useState<Error | null>(null)

  const cancelledRef = useRef(false)

  const fetchOnce = useCallback(async (signalCancelled: () => boolean) => {
    try {
      const next = await myInstances(filter)
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

    return () => {
      cancelledRef.current = true
      window.clearInterval(handle)
    }
  }, [fetchOnce])

  return { data, loading, error, refresh }
}
