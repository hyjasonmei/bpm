/**
 * NoteEditorModal — generic textarea-in-modal for any free-text note
 * field in the wizard. Reusable across:
 *
 *   - FORMS field.note (the original driver)
 *   - VARIABLES variable.description (planned)
 *   - NOTES step body (planned)
 *
 *   <NoteEditorModal
 *     open={open}
 *     title="欄位備註 — 事由"
 *     initial={field.note?.['zh-TW'] ?? ''}
 *     placeholder="CEL 寫不下的需求…"
 *     onCancel={() => setOpen(false)}
 *     onCommit={(v) => { onChange({ ...field, note: v ? { 'zh-TW': v } : undefined }); setOpen(false) }}
 *   />
 *
 * Buffered: edits commit on Save, dismissed on Cancel.
 */
import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'

interface Props {
  open: boolean
  title: string
  initial: string
  placeholder?: string
  /** Optional helper rendered above the textarea — explain who reads
   *  the note, what format works best, etc. */
  helper?: React.ReactNode
  onCancel: () => void
  onCommit: (value: string) => void
}

export function NoteEditorModal({
  open, title, initial, placeholder, helper, onCancel, onCommit,
}: Props) {
  const [value, setValue] = useState(initial)
  useEffect(() => { if (open) setValue(initial) }, [open, initial])

  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="md"
      footer={
        <>
          <div className="mr-auto text-xs text-ink-muted">
            {value.length} 字
          </div>
          <Button variant="ghost" onClick={onCancel}>Cancel</Button>
          <Button variant="primary" onClick={() => onCommit(value.trim())}>Save</Button>
        </>
      }
    >
      <div className="space-y-3 p-4">
        {helper && <div className="text-xs text-ink-muted">{helper}</div>}
        <textarea
          autoFocus
          value={value}
          onChange={e => setValue(e.target.value)}
          placeholder={placeholder ?? '寫下要交代給 chef / 驗收者的內容…'}
          rows={10}
          className="block w-full rounded border border-rule bg-white p-3 text-sm leading-relaxed text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
        />
      </div>
    </Modal>
  )
}
