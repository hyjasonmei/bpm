import { useEffect, useState } from 'react'
import { getActingFor } from '@/lib/api/delegation'

/**
 * User ids the current viewer may act on behalf of right now (active inbound
 * delegations). Case-detail components OR this into their "is the viewer the
 * current assignee?" check so a delegate sees the approve/reject buttons on the
 * delegator's pending cases. Best-effort: empty on error.
 */
export function useDelegatedFor(): string[] {
  const [ids, setIds] = useState<string[]>([])
  useEffect(() => {
    let cancelled = false
    void getActingFor().then(r => { if (!cancelled) setIds(r) }).catch(() => undefined)
    return () => { cancelled = true }
  }, [])
  return ids
}
