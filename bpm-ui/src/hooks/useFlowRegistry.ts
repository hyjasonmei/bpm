import { useEffect, useState } from 'react'
import { apiFetch } from '@/lib/apiFetch'
import { FORMS, type FormCode } from '@/lib/workflow'

/** Mirror of bpm-svc's FlowRegistryEntry record. */
export interface FlowRegistryEntry {
  flowCode: string
  version: number
  /** Stringified Bpm.Admin.Domain.Flows.FlowState — see SharedFlowState. */
  state: 'Draft' | 'Submitted' | 'Cooking' | 'OnHold' | 'Committed' | 'Approved' | 'Rejected' | 'Retired' | 'Published' | string
  displayName: string
  updatedAt: string
  /** Per-flow curated lucide icon name for the launcher tile, null = default. */
  iconKey: string | null
  /** Per-flow launcher sort weight within its group (low → high; ties on flowCode). */
  displayOrder: number
  // PR-G3: launcher group metadata, null when unassigned (or the
  // assigned group was soft-deleted on the admin side).
  groupCode: string | null
  groupDisplayName: Record<string, string> | null
  groupIcon: string | null
  groupSortOrder: number | null
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

/**
 * Resolve a flow's end-user display name. Admin's Flow.DisplayName (served by
 * /api/flow-registry) is the single source of truth — the compile-time FORMS
 * label is only a fallback while the registry is still loading or for a flow
 * with no admin name set. Pass `version` to label a specific historical case;
 * omit it for the launcher (latest version wins).
 */
export function resolveFlowLabel(
  entries: FlowRegistryEntry[] | null,
  code: string,
  version?: number,
): string {
  const fallback = FORMS[code as FormCode]?.zhLabel ?? code
  if (!entries) return fallback
  let chosen: FlowRegistryEntry | undefined
  for (const e of entries) {
    if (e.flowCode !== code) continue
    if (version != null) {
      if (e.version === version) { chosen = e; break }
    } else if (!chosen || e.version > chosen.version) {
      chosen = e
    }
  }
  return chosen?.displayName || fallback
}

/** Hook form of {@link resolveFlowLabel}, bound to the cached registry. */
export function useFlowLabel(): (code: string, version?: number) => string {
  const { entries } = useFlowRegistry()
  return (code, version) => resolveFlowLabel(entries, code, version)
}
