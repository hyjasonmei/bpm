import * as React from 'react'
import * as Dialog from '@radix-ui/react-dialog'

import { cn } from '@/lib/cn'

export interface ModalProps {
  open: boolean
  onClose: () => void
  /** Disable the click-on-backdrop + Escape close path (destructive confirms). */
  dismissOnBackdrop?: boolean
  /** ARIA labelling — pass the heading element's id. */
  ariaLabelledBy?: string
  /** Optional max-width class for the panel (e.g. 'max-w-md', 'max-w-3xl'). Default 'max-w-md'. */
  panelClassName?: string
  children: React.ReactNode
}

/**
 * Thin wrapper over Radix Dialog. We get for free:
 *  - Portal rendering into `document.body` (backdrop never clipped by ancestor `overflow`/`transform`)
 *  - Body-scroll lock while open
 *  - Focus trap + restore-on-close
 *  - Escape close + topmost-modal-only handling
 *  - Accessible role / aria-modal / aria-labelledby plumbing
 *
 * Every overlay in bpm-ui should sit on top of this (per the openspec
 * change `fix-modal-fullpage-backdrop`). Avoid hand-rolling `fixed
 * inset-0 z-…` overlays.
 */
export function Modal({
  open,
  onClose,
  dismissOnBackdrop = true,
  ariaLabelledBy,
  panelClassName,
  children,
}: ModalProps) {
  return (
    <Dialog.Root
      open={open}
      onOpenChange={next => {
        if (!next) onClose()
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-black/50 data-[state=open]:animate-in data-[state=closed]:animate-out" />
        <Dialog.Content
          aria-labelledby={ariaLabelledBy}
          onInteractOutside={e => {
            if (!dismissOnBackdrop) e.preventDefault()
          }}
          onEscapeKeyDown={e => {
            if (!dismissOnBackdrop) e.preventDefault()
          }}
          className={cn(
            'fixed left-1/2 top-1/2 z-50 w-full -translate-x-1/2 -translate-y-1/2 overflow-hidden rounded-lg border border-rule bg-card shadow-2xl outline-none',
            panelClassName ?? 'max-w-md',
          )}
        >
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}
