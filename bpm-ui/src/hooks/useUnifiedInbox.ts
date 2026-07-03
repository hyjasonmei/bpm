import { useCallback, useEffect, useState } from 'react'

import { apiFetch } from '@/lib/apiFetch'

/** Row returned by /api/inbox/{mine,pending} — matches Bpm.Application.Inbox.InboxRow. */
export interface InboxRow {
  caseId: string
  flowCode: string
  flowVersion: number
  title: string
  status: string           // per-flow localized display text — do NOT parse
  lifecycle: string        // canonical: 'Open' | 'Completed' | 'Cancelled' | 'Rejected' — parse THIS
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
    // The inbox is identity-scoped. Switching persona re-mints the JWT to a
    // different user without remounting this hook, so without an explicit
    // signal we'd keep showing the previous identity's rows until the next
    // poll tick (up to 30s). Refetch immediately when the identity changes
    // or an impersonation swap-back fires.
    const onIdentityChanged = () => { void load() }
    window.addEventListener('bpm:identity-changed', onIdentityChanged)
    window.addEventListener('bpm:impersonation-ended', onIdentityChanged)
    return () => {
      cancelled = true
      window.clearInterval(id)
      window.removeEventListener('bpm:identity-changed', onIdentityChanged)
      window.removeEventListener('bpm:impersonation-ended', onIdentityChanged)
    }
  }, [load])

  return { ...state, refresh: load }
}

export const useInboxMine = () => useInboxEndpoint('/api/inbox/mine')
export const useInboxPending = () => useInboxEndpoint('/api/inbox/pending')
