import { useEffect, useState } from 'react'
import { UserCheck } from 'lucide-react'
import { getActingForDetail, type ActingFor } from '@/lib/api/delegation'

/**
 * Persistent info bar shown when the signed-in user is an active delegate for
 * one or more colleagues. The delegate side otherwise has zero visibility that
 * they can act on someone else's cases — the header 代理人 button only shows
 * *outgoing* delegations (who you delegated to), so an inbound delegate sees
 * nothing. This surfaces the inbound side. Calm/informational (not the red
 * impersonation banner — delegation is a normal, sanctioned mode).
 */
export function ActingForBanner() {
  const [acting, setActing] = useState<ActingFor[]>([])
  useEffect(() => {
    let cancelled = false
    void getActingForDetail()
      .then(r => { if (!cancelled) setActing(r) })
      .catch(() => undefined)
    return () => { cancelled = true }
  }, [])

  if (acting.length === 0) return null
  const names = acting.map(a => a.delegatorName ?? '某位同事').join('、')

  return (
    <div className="no-print flex items-center gap-2 border-b border-sky-200 bg-sky-50 px-4 py-2 text-xs font-medium text-sky-800">
      <UserCheck className="h-4 w-4 shrink-0" />
      <span>
        你目前是 <strong>{names}</strong> 的代理人 — 可代為簽核其待辦案件。
      </span>
    </div>
  )
}
