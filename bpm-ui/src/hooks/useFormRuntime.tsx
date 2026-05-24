import { useCallback, useState } from 'react'
import { useFlowSubmit } from './useFlowSubmit'
import { useFlowTask } from './useFlowTask'
import type { FormCode } from '@/lib/workflow'
import type { Decision } from '@/types/process'

/**
 * Common props the 11 demo forms accept so the parent (App.tsx / Inbox in PR-L3)
 * can plumb create-vs-task mode in one shape.
 *
 * `mode`:   'create' (default) — form is editable and submission starts a new instance.
 *           'task'             — form is read-only and renders Approve/Reject/Return.
 * `taskId`: required when mode === 'task'.
 * `onSubmitted`: fired after a successful start / submit / return action so the
 *                parent can refresh inbox or navigate.
 */
export interface FormRuntimeProps {
  mode?: 'create' | 'task'
  taskId?: string | null
  onSubmitted?: (result: { instanceId?: string; firstTaskId?: string }) => void
}

/**
 * Convenience wrapper used by each form. Picks the right hook based on mode
 * and unifies the toast / error surface.
 *
 * Returns a uniform `act` function:
 *  - In create mode: call `act.create(formData)` to start an instance.
 *  - In task mode:   call `act.approve(comment)` / `act.reject(comment)` /
 *                    `act.return(comment)` / `act.submitUserTask(formDataPatch)`.
 *
 * `task` exposes the loaded TaskWithFormDto for read-only rendering.
 * `pending` covers both create-submit and task-action in-flight states.
 */
export function useFormRuntime(specCode: FormCode, props: FormRuntimeProps) {
  const { mode = 'create', taskId = null, onSubmitted } = props
  const create = useFlowSubmit()
  const task = useFlowTask(mode === 'task' ? (taskId ?? null) : null)

  const [toast, setToast] = useState<string | null>(null)
  const fireToast = useCallback((m: string) => {
    setToast(m)
    window.setTimeout(() => setToast(null), 3000)
  }, [])

  // Notify `useMyTasks` / `useMyInstances` (Home inbox + cases table) so the
  // new task appears immediately after a submit rather than waiting up to
  // 30 s for the next poll. See openspec change `redirect-home-after-submit`.
  const invalidateTasks = useCallback(() => {
    window.dispatchEvent(new CustomEvent('bpm:tasks-invalidate'))
  }, [])

  const submitCreate = useCallback(async (formData: unknown) => {
    try {
      const r = await create.submit(specCode, formData)
      invalidateTasks()
      fireToast(`Submitted! Instance ${r.instanceId.slice(0, 8)} • first task ${r.firstTaskId.slice(0, 8)}`)
      onSubmitted?.({ instanceId: r.instanceId, firstTaskId: r.firstTaskId })
      return r
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      fireToast(`Submit failed: ${msg}`)
      throw e
    }
  }, [create, specCode, fireToast, invalidateTasks, onSubmitted])

  const approve = useCallback(async (comment: string) => {
    try {
      await task.submit({ decision: 'Approve' as Decision, comment: comment || undefined })
      invalidateTasks()
      fireToast('Approved.')
      onSubmitted?.({})
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      fireToast(`Approve failed: ${msg}`)
      throw e
    }
  }, [task, fireToast, invalidateTasks, onSubmitted])

  const reject = useCallback(async (comment: string) => {
    try {
      await task.submit({ decision: 'Reject' as Decision, comment })
      invalidateTasks()
      fireToast('Rejected.')
      onSubmitted?.({})
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      fireToast(`Reject failed: ${msg}`)
      throw e
    }
  }, [task, fireToast, invalidateTasks, onSubmitted])

  const returnAct = useCallback(async (comment: string) => {
    try {
      await task.returnTask(comment)
      invalidateTasks()
      fireToast('Returned to previous step.')
      onSubmitted?.({})
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      fireToast(`Return failed: ${msg}`)
      throw e
    }
  }, [task, fireToast, invalidateTasks, onSubmitted])

  const submitUserTask = useCallback(async (formDataPatch?: unknown) => {
    try {
      await task.submit({ formDataPatch })
      invalidateTasks()
      fireToast('Submitted.')
      onSubmitted?.({})
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      fireToast(`Submit failed: ${msg}`)
      throw e
    }
  }, [task, fireToast, invalidateTasks, onSubmitted])

  return {
    mode,
    toast,
    fireToast,
    /** In task mode, the loaded task snapshot. null in create mode or while loading. */
    task: task.data,
    taskLoading: task.loading,
    taskError: task.error,
    /** True while either a create or task action is in-flight. */
    pending: create.pending || task.pending,
    createError: create.error,
    submitCreate,
    approve,
    reject,
    returnTask: returnAct,
    submitUserTask,
  }
}

/** Renders the floating bottom-right toast. */
export function FlowToast({ message }: { message: string | null }) {
  if (!message) return null
  return (
    <div className="fixed bottom-6 right-6 z-50 rounded-lg bg-slate-800 px-4 py-2.5 text-sm text-white shadow-2xl">
      {message}
    </div>
  )
}
