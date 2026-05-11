import { useCallback, useEffect, useState } from 'react'
import {
  claimTask,
  getTask,
  returnTask as apiReturn,
  submitTask as apiSubmit,
} from '@/lib/api/process'
import type { Decision, TaskWithFormDto } from '@/types/process'

export interface UseFlowTaskResult {
  data: TaskWithFormDto | null
  loading: boolean
  error: string | null
  pending: boolean
  /** Refetch the task snapshot (e.g. after an external event). */
  refresh: () => Promise<void>
  submit: (body: { formDataPatch?: unknown; decision?: Decision; comment?: string }) => Promise<void>
  returnTask: (comment: string) => Promise<void>
  claim: () => Promise<void>
}

/**
 * Wraps the task-mode form path: load TaskWithFormDto, then drive
 * Approve/Reject/Return/Submit via the existing API client.
 *
 * Pass `null` for `taskId` to disable; transitions to a non-null id refetch.
 * `pending` is true while a submit / return / claim call is in-flight.
 */
export function useFlowTask(taskId: string | null): UseFlowTaskResult {
  const [data, setData] = useState<TaskWithFormDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const fetchOnce = useCallback(async (id: string, isCancelled: () => boolean) => {
    setLoading(true)
    setError(null)
    try {
      const d = await getTask(id)
      if (isCancelled()) return
      setData(d)
    } catch (e: unknown) {
      if (isCancelled()) return
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      if (!isCancelled()) setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (!taskId) {
      setData(null)
      setError(null)
      setLoading(false)
      return
    }
    let cancelled = false
    void fetchOnce(taskId, () => cancelled)
    return () => { cancelled = true }
  }, [taskId, fetchOnce])

  const refresh = useCallback(async () => {
    if (!taskId) return
    let cancelled = false
    await fetchOnce(taskId, () => cancelled)
  }, [taskId, fetchOnce])

  const submit = useCallback(async (body: {
    formDataPatch?: unknown
    decision?: Decision
    comment?: string
  }) => {
    if (!taskId) throw new Error('useFlowTask.submit: no taskId')
    setPending(true)
    try {
      await apiSubmit(taskId, body)
    } finally {
      setPending(false)
    }
  }, [taskId])

  const returnTask = useCallback(async (comment: string) => {
    if (!taskId) throw new Error('useFlowTask.returnTask: no taskId')
    setPending(true)
    try {
      await apiReturn(taskId, comment)
    } finally {
      setPending(false)
    }
  }, [taskId])

  const claim = useCallback(async () => {
    if (!taskId) throw new Error('useFlowTask.claim: no taskId')
    setPending(true)
    try {
      await claimTask(taskId)
    } finally {
      setPending(false)
    }
  }, [taskId])

  return { data, loading, error, pending, refresh, submit, returnTask, claim }
}
