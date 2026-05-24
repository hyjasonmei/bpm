import { AlertTriangle, X } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'

export interface ConfirmDialogProps {
  open: boolean
  title: string
  titleZh?: string
  description?: string
  confirmText?: string
  cancelText?: string
  tone?: 'danger' | 'default'
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Canonical confirm dialog. Sits on top of the `<Modal>` primitive so the
 * backdrop covers the entire viewport regardless of mount point.
 */
export function ConfirmDialog({
  open,
  title,
  titleZh,
  description,
  confirmText = 'Confirm',
  cancelText = 'Cancel / 取消',
  tone = 'danger',
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const titleId = 'confirm-dialog-title'
  return (
    <Modal
      open={open}
      onClose={onCancel}
      ariaLabelledBy={titleId}
      // Destructive confirms shouldn't disappear on a stray backdrop click.
      dismissOnBackdrop={tone !== 'danger'}
    >
      <div className="relative">
        <button
          type="button"
          onClick={onCancel}
          className="absolute right-4 top-4 rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
          aria-label="Close"
        >
          <X className="h-4 w-4" />
        </button>
        <div className="p-6">
          <div className="flex items-start gap-3">
            {tone === 'danger' && (
              <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-100">
                <AlertTriangle className="h-5 w-5 text-danger" />
              </span>
            )}
            <div className="flex-1 pt-1">
              <h3 id={titleId} className="text-base font-bold text-ink">{title}</h3>
              {titleZh && <p className="text-sm text-ink-muted">{titleZh}</p>}
              {description && <p className="mt-2 text-sm leading-relaxed text-slate-600">{description}</p>}
            </div>
          </div>
        </div>
        <div className="flex justify-end gap-2 border-t border-rule bg-slate-50/50 px-6 py-3">
          <Button variant="ghost" onClick={onCancel}>{cancelText}</Button>
          <Button variant={tone === 'danger' ? 'destructive' : 'primary'} onClick={onConfirm}>{confirmText}</Button>
        </div>
      </div>
    </Modal>
  )
}
