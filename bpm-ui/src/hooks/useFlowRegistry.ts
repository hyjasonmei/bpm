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

/**
 * Tie-break two rows of the same flowCode. Higher version wins; on the SAME
 * version (a retired old cook + a re-published new cook can share
 * code+version), Published wins, then the newer updatedAt. Without this a
 * stale Retired row that happens to sort first shadows the live one —
 * hiding the flow from the launcher and collapsing its label to the code.
 */
function preferEntry(a: FlowRegistryEntry, b: FlowRegistryEntry): FlowRegistryEntry {
  if (a.version !== b.version) return b.version > a.version ? b : a
  const aPub = a.state === 'Published'
  const bPub = b.state === 'Published'
  if (aPub !== bPub) return bPub ? b : a
  return Date.parse(b.updatedAt) >= Date.parse(a.updatedAt) ? b : a
}

/** Latest non-deleted version per flowCode. Useful for the launcher. */
export function latestPerCode(entries: FlowRegistryEntry[] | null): Map<string, FlowRegistryEntry> {
  const out = new Map<string, FlowRegistryEntry>()
  if (!entries) return out
  for (const e of entries) {
    const cur = out.get(e.flowCode)
    out.set(e.flowCode, cur ? preferEntry(cur, e) : e)
  }
  return out
}

/** The registry row for an exact (flowCode, version), or undefined. */
export function entryForVersion(
  entries: FlowRegistryEntry[] | null,
  code: string,
  version: number,
): FlowRegistryEntry | undefined {
  if (!entries) return undefined
  let chosen: FlowRegistryEntry | undefined
  for (const e of entries) {
    if (e.flowCode !== code || e.version !== version) continue
    chosen = chosen ? preferEntry(chosen, e) : e
  }
  return chosen
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
  // Platform (non-chef) inbox sources — never in the admin registry.
  if (code === 'ATTENDANCE_CORRECTION') return '補打卡'
  const fallback = FORMS[code as FormCode]?.zhLabel ?? code
  if (!entries) return fallback
  let chosen: FlowRegistryEntry | undefined
  for (const e of entries) {
    if (e.flowCode !== code) continue
    if (version != null && e.version !== version) continue
    chosen = chosen ? preferEntry(chosen, e) : e
  }
  return chosen?.displayName || fallback
}

/** Hook form of {@link resolveFlowLabel}, bound to the cached registry. */
export function useFlowLabel(): (code: string, version?: number) => string {
  const { entries } = useFlowRegistry()
  return (code, version) => resolveFlowLabel(entries, code, version)
}
