import { useState } from 'react'
import { X, Eye, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input, Textarea, Field } from '@/components/ui/form'
import { startImpersonation } from '@/lib/api/impersonation'
import { enterImpersonation } from '@/lib/impersonationToken'

interface Props {
  open: boolean
  onClose: () => void
}

export function ImpersonationModal({ open, onClose }: Props) {
  const [targetUserId, setTargetUserId] = useState('')
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  if (!open) return null

  async function submit() {
    if (!targetUserId.trim() || !reason.trim()) {
      setErr('Both fields are required.')
      return
    }
    setBusy(true)
    setErr(null)
    try {
      const result = await startImpersonation(targetUserId.trim(), reason.trim())
      enterImpersonation(result.token)
      window.location.reload()
    } catch (e) {
      setErr((e as Error).message)
      setBusy(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-md rounded-lg border border-rule bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-rule px-4 py-3">
          <div className="flex items-center gap-2">
            <Eye className="h-4 w-4 text-amber-600" />
            <h2 className="text-sm font-semibold text-ink">Act as another user</h2>
          </div>
          <button onClick={onClose} className="text-ink-muted hover:text-ink"><X className="h-4 w-4" /></button>
        </div>
        <div className="space-y-4 p-4">
          <Field label="Target user id (UUID)" hint="POC: paste a UUID. Future: user picker.">
            <Input value={targetUserId} onChange={e => setTargetUserId(e.target.value)} placeholder="00000000-0000-0000-0000-000000000000" />
          </Field>
          <Field label="Reason" required hint="Logged in audit trail.">
            <Textarea rows={3} value={reason} onChange={e => setReason(e.target.value)} placeholder="e.g. UAT — verify Wilson's leave flow" />
          </Field>
          {err && <p className="text-xs text-danger">{err}</p>}
          <div className="flex justify-end gap-2 border-t border-rule pt-3">
            <Button variant="outline" size="sm" onClick={onClose} disabled={busy}>Cancel</Button>
            <Button variant="primary" size="sm" onClick={submit} disabled={busy}>
              {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Start session (30 min)
            </Button>
          </div>
        </div>
      </div>
    </div>
  )
}
