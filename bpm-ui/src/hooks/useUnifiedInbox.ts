import { useCallback, useEffect, useState } from 'react'

import { apiFetch } from '@/lib/apiFetch'

/** Row returned by /api/inbox/{mine,pending} — matches Bpm.Application.Inbox.InboxRow. */
export interface InboxRow {
  caseId: string
  flowCode: string
  flowVersion: number
  title: string
  status: string
  submittedAt: string
  lastActivityAt: string
  detailUrl: string
}

interface State {
  data: InboxRow[] | null
  loading: boolean
  error: Error | null
}

function useInboxEndpoint(path: '/api/inbox/mine' | '/api/inbox/pending'): State & { refresh: () => Promise<void> } {
  const [state, setState] = useState<State>({ data: null, loading: true, error: null })

  const load = useCallback(async () => {
    setState(s => ({ ...s, loading: true, error: null }))
    try {
      const res = await apiFetch(path)
      if (!res.ok) throw new Error(`${path} ${res.status}`)
      const json = (await res.json()) as InboxRow[]
      setState({ data: json, loading: false, error: null })
    } catch (err) {
      setState({ data: null, loading: false, error: err as Error })
    }
  }, [path])

  useEffect(() => {
    let cancelled = false
    void (async () => {
      await load()
      if (cancelled) return
    })()
    const id = window.setInterval(load, 30_000)
    return () => {
      cancelled = true
      window.clearInterval(id)
    }
  }, [load])

  return { ...state, refresh: load }
}

export const useInboxMine = () => useInboxEndpoint('/api/inbox/mine')
export const useInboxPending = () => useInboxEndpoint('/api/inbox/pending')
