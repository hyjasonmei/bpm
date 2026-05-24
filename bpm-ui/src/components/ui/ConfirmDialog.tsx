import { useEffect } from 'react'
import { AlertTriangle, X } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface ConfirmDialogProps {
  open: boolean
  title: string
  /** Body content — string is wrapped in a <p>; ReactNode passed through verbatim. */
  body: React.ReactNode
  /** Defaults to "確認". */
  confirmLabel?: string
  /** Defaults to "取消". */
  cancelLabel?: string
  /** When true the confirm button uses the destructive variant. */
  destructive?: boolean
  /** Disable both buttons while async work is in flight. */
  busy?: boolean
  onConfirm: () => void | Promise<void>
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  body,
  confirmLabel = '確認',
  cancelLabel = '取消',
  destructive = false,
  busy = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  useEffect(() => {
    if (!open) return
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onCancel() }
    document.addEventListener('keydown', onEsc)
    return () => document.removeEventListener('keydown', onEsc)
  }, [open, onCancel, busy])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40 p-4 fade-in"
      onClick={busy ? undefined : onCancel}
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-dialog-title"
    >
      <div
        className="w-full max-w-md rounded-lg border border-rule bg-card shadow-2xl"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-start justify-between border-b border-rule px-5 py-3">
          <div className="flex items-center gap-2">
            {destructive && <AlertTriangle className="h-4 w-4 text-danger" />}
            <h2 id="confirm-dialog-title" className="text-sm font-semibold text-ink">{title}</h2>
          </div>
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="text-ink-muted hover:text-ink disabled:opacity-40"
            aria-label="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="px-5 py-4 text-sm text-ink">
          {typeof body === 'string' ? <p>{body}</p> : body}
        </div>
        <div className="flex justify-end gap-2 border-t border-rule bg-slate-50/50 px-5 py-3">
          <Button variant="outline" size="sm" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>
          <Button
            variant={destructive ? 'destructive' : 'primary'}
            size="sm"
            onClick={onConfirm}
            disabled={busy}
          >
            {busy ? '處理中…' : confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  )
}
