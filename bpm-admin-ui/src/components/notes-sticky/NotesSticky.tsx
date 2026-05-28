import { useState } from 'react'
import { StickyNote } from 'lucide-react'
import { cn } from '@/lib/cn'
import { NoteEditorModal } from '@/components/note-editor/NoteEditorModal'

interface Props {
  notes: string
  onChange: (next: string) => void
}

/**
 * Floating button anchored to the top-right of the AI Kitchen wizard.
 * Opens a modal to edit `draft.notes` — the free-text instruction the
 * NOTES step used to host. Lives outside the stepper so the user can
 * jot a chef-facing note from any step without losing place.
 *
 * Red dot when notes have content; click to open the buffered editor.
 */
export function NotesSticky({ notes, onChange }: Props) {
  const [open, setOpen] = useState(false)
  const has = notes.trim().length > 0
  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        title={has ? '已寫了 chef 備註，點此編輯' : '寫給 chef 看的補充說明'}
        className={cn(
          'relative inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs font-medium transition-colors',
          has
            ? 'border-accent/30 bg-accent/5 text-accent hover:border-accent hover:bg-accent/10'
            : 'border-rule bg-card text-ink-muted hover:border-primary hover:text-primary',
        )}
      >
        <StickyNote className="h-3.5 w-3.5" />
        <span>給 chef 的備註</span>
        {has && (
          <span className="absolute -right-1 -top-1 h-2 w-2 rounded-full bg-accent ring-2 ring-card" />
        )}
      </button>
      <NoteEditorModal
        open={open}
        title="給 chef / 驗收者的備註"
        initial={notes}
        helper={
          <>
            這段文字 chef（生 code 的 AI）會逐字讀，請寫產品 spec 寫不下的業務脈絡 — 譬如「這版客戶只用 zh-TW」「對接的 SAP 系統時區是 UTC+8」「審核第一輪不通過要走年度預算 review」之類。<br />
            一般使用者看不到此內容。
          </>
        }
        placeholder="這條流程跟客戶溝通時帶到的補充細節…"
        onCancel={() => setOpen(false)}
        onCommit={v => { onChange(v); setOpen(false) }}
      />
    </>
  )
}
