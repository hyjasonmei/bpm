/**
 * Modal shell — header + scrollable body + optional footer, with the
 * usual fixed backdrop, ESC-to-close, and click-outside-to-close. Use
 * for any wizard dialog that wants the same look as ImportModal /
 * PrincipalPicker without re-implementing the chrome.
 *
 *   <Modal open={open} onClose={close} title="選擇 principal" size="lg"
 *          footer={<><Button variant="ghost" onClick={close}>Cancel</Button>
 *                    <Button variant="primary" onClick={commit}>Select</Button></>}>
 *     {body}
 *   </Modal>
 *
 * The body slot scrolls if its content exceeds the modal height; the
 * shell itself never scrolls (so header / footer stay pinned).
 */
import { useEffect } from 'react'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'

type Size = 'sm' | 'md' | 'lg' | 'xl'

const SIZE_CLS: Record<Size, string> = {
  sm: 'max-w-md',
  md: 'max-w-2xl',
  lg: 'max-w-3xl',
  xl: 'max-w-5xl',
}

interface Props {
  open: boolean
  onClose: () => void
  title: React.ReactNode
  /** Optional element rendered to the right of the title (e.g. badge, action). */
  titleSlot?: React.ReactNode
  children: React.ReactNode
  footer?: React.ReactNode
  size?: Size
  /** Hide the × button (e.g. when only Cancel/Save should dismiss). */
  hideClose?: boolean
  /** Disable ESC-to-close + backdrop click. Use sparingly. */
  dismissable?: boolean
  /** Extra classes on the inner panel. */
  className?: string
}

export function Modal({
  open, onClose, title, titleSlot, children, footer,
  size = 'md', hideClose = false, dismissable = true, className,
}: Props) {
  useEffect(() => {
    if (!open || !dismissable) return
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, dismissable, onClose])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4"
      onClick={dismissable ? onClose : undefined}
    >
      <div
        className={cn(
          'flex max-h-[85vh] w-full flex-col rounded-lg bg-white shadow-2xl',
          SIZE_CLS[size],
          className,
        )}
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between gap-3 border-b border-rule px-5 py-3">
          <h3 className="flex-1 text-base font-bold text-ink">{title}</h3>
          {titleSlot}
          {!hideClose && (
            <button
              onClick={onClose}
              className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-700"
              title="關閉"
            >
              <X className="h-4 w-4" />
            </button>
          )}
        </div>

        <div className="min-h-0 flex-1 overflow-auto">{children}</div>

        {footer && (
          <div className="flex items-center justify-end gap-2 border-t border-rule bg-slate-50/50 px-4 py-3">
            {footer}
          </div>
        )}
      </div>
    </div>
  )
}
