import { useEffect, useState } from 'react'
import { apiFetch } from '@/lib/apiFetch'

/** Mirror of bpm-svc's FlowRegistryEntry record. */
export interface FlowRegistryEntry {
  flowCode: string
  version: number
  /** Stringified Bpm.Admin.Domain.Flows.FlowState — see SharedFlowState. */
  state: 'Draft' | 'Submitted' | 'Cooking' | 'OnHold' | 'Committed' | 'Approved' | 'Rejected' | 'Retired' | string
  displayName: string
  updatedAt: string
}

interface State {
  entries: FlowRegistryEntry[] | null
  loading: boolean
  error: Error | null
}

/**
 * GET /api/flow-registry — returns every Admin_Flow row visible to
 * bpm-svc. Callers (launcher, case-detail) usually filter to the
 * latest version per flowCode and check the state. Cached at the
 * module level so re-mounting the launcher doesn't refetch.
 */
let cache: FlowRegistryEntry[] | null = null
let inflight: Promise<FlowRegistryEntry[]> | null = null

export function useFlowRegistry(): State {
  const [state, setState] = useState<State>(() => ({
    entries: cache,
    loading: cache === null,
    error: null,
  }))

  useEffect(() => {
    if (cache !== null) return
    let cancelled = false
    const load = async () => {
      try {
        if (!inflight) {
          inflight = (async () => {
            const res = await apiFetch('/api/flow-registry')
            if (!res.ok) throw new Error(`flow-registry ${res.status}`)
            return (await res.json()) as FlowRegistryEntry[]
          })()
        }
        const entries = await inflight
        cache = entries
        if (!cancelled) setState({ entries, loading: false, error: null })
      } catch (err) {
        if (!cancelled) setState({ entries: null, loading: false, error: err instanceof Error ? err : new Error(String(err)) })
      } finally {
        inflight = null
      }
    }
    void load()
    return () => { cancelled = true }
  }, [])

  return state
}

/** Latest non-deleted version per flowCode. Useful for the launcher. */
export function latestPerCode(entries: FlowRegistryEntry[] | null): Map<string, FlowRegistryEntry> {
  const out = new Map<string, FlowRegistryEntry>()
  if (!entries) return out
  for (const e of entries) {
    const cur = out.get(e.flowCode)
    if (!cur || e.version > cur.version) out.set(e.flowCode, e)
  }
  return out
}
