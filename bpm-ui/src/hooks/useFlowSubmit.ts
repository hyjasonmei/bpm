import { useCallback, useState } from 'react'
import { startProcess } from '@/lib/api/process'
import type { FormCode } from '@/lib/workflow'

interface UseFlowSubmitState {
  pending: boolean
  error: string | null
  result: { instanceId: string; firstTaskId: string } | null
}

/**
 * Wraps `POST /api/processes` for the create-mode form path.
 *
 * Each call resets state. The `submit` function returns the result for the
 * caller to navigate / show a toast; throws if the API errored. State is
 * also exposed so the form can render a spinner / error message.
 */
export function useFlowSubmit() {
  const [state, setState] = useState<UseFlowSubmitState>({
    pending: false,
    error: null,
    result: null,
  })

  const submit = useCallback(async (specCode: FormCode, formData: unknown) => {
    setState({ pending: true, error: null, result: null })
    try {
      const r = await startProcess(specCode, formData)
      setState({ pending: false, error: null, result: r })
      return r
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setState({ pending: false, error: msg, result: null })
      throw e
    }
  }, [])

  return { ...state, submit }
}
