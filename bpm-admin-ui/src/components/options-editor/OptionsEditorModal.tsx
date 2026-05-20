/**
 * OptionsEditorModal — edit the option list of a `select` / `multiselect`
 * field. Renders a table with `value` and `label` columns, reorder buttons,
 * and a row remove. Buffered: changes only commit when user clicks Save.
 */
import { useEffect, useState } from 'react'
import { ArrowDown, ArrowUp, Plus, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/modal'

export interface OptionItem {
  value: string
  label: string
}

interface Props {
  open: boolean
  fieldLabel?: string
  initial: OptionItem[]
  onCancel: () => void
  onCommit: (next: OptionItem[]) => void
}

export function OptionsEditorModal({ open, fieldLabel, initial, onCancel, onCommit }: Props) {
  const [rows, setRows] = useState<OptionItem[]>(initial)

  // Reset buffer if the modal opens with a different initial set.
  useEffect(() => { if (open) setRows(initial) }, [open, initial])

  function update(i: number, patch: Partial<OptionItem>) {
    setRows(rows.map((r, idx) => idx === i ? { ...r, ...patch } : r))
  }
  function remove(i: number) {
    setRows(rows.filter((_, idx) => idx !== i))
  }
  function add() {
    setRows([...rows, { value: '', label: '' }])
  }
  function move(i: number, dir: -1 | 1) {
    const j = i + dir
    if (j < 0 || j >= rows.length) return
    const next = rows.slice()
    ;[next[i], next[j]] = [next[j], next[i]]
    setRows(next)
  }

  const dupValues = rows
    .map(r => r.value)
    .filter((v, i, arr) => v && arr.indexOf(v) !== i)
  const hasDup = dupValues.length > 0
  const missingValue = rows.some(r => !r.value.trim())
  const missingLabel = rows.some(r => !r.label.trim())
  const canSave = rows.length > 0 && !hasDup && !missingValue && !missingLabel

  return (
    <Modal
      open={open}
      onClose={onCancel}
      size="md"
      title={fieldLabel ? `選項清單 — ${fieldLabel}` : '選項清單'}
      footer={
        <>
          <div className="mr-auto text-xs text-ink-muted">
            {rows.length} 個選項
            {hasDup && <span className="ml-2 text-warn">· 有重複 value</span>}
            {(missingValue || missingLabel) && <span className="ml-2 text-warn">· 有空欄位</span>}
          </div>
          <Button variant="ghost" onClick={onCancel}>Cancel</Button>
          <Button variant="primary" disabled={!canSave} onClick={() => onCommit(rows)}>Save</Button>
        </>
      }
    >
      <div className="p-4">
        <p className="mb-3 text-xs text-ink-muted">
          <span className="font-mono">value</span> 是存進 spec / DB 的識別字串（建議英數底線）；<span className="font-mono">label</span> 是 user 看到的顯示文字。可以中英文混用。
        </p>

        {rows.length === 0 && (
          <div className="rounded border border-dashed border-rule p-6 text-center text-xs text-ink-faint">
            還沒有選項。下方「加選項」開始。
          </div>
        )}

        {rows.length > 0 && (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
                <th className="w-10 pb-2"></th>
                <th className="pb-2 font-normal">Value</th>
                <th className="pb-2 font-normal">Label</th>
                <th className="w-12 pb-2"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-rule">
              {rows.map((row, i) => {
                const isDup = !!row.value && rows.filter(r => r.value === row.value).length > 1
                return (
                  <tr key={i}>
                    <td className="py-1.5 pr-1 text-center">
                      <div className="flex flex-col items-center gap-0.5">
                        <button
                          type="button"
                          onClick={() => move(i, -1)}
                          disabled={i === 0}
                          className="text-ink-faint hover:text-ink disabled:opacity-20"
                          title="上移"
                        ><ArrowUp className="h-3 w-3" /></button>
                        <button
                          type="button"
                          onClick={() => move(i, 1)}
                          disabled={i === rows.length - 1}
                          className="text-ink-faint hover:text-ink disabled:opacity-20"
                          title="下移"
                        ><ArrowDown className="h-3 w-3" /></button>
                      </div>
                    </td>
                    <td className="py-1.5 pr-2">
                      <input
                        value={row.value}
                        onChange={e => update(i, { value: e.target.value })}
                        placeholder="annual_leave"
                        className={[
                          'w-full rounded border bg-white px-2 py-1 font-mono text-xs text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20',
                          isDup ? 'border-warn' : 'border-rule',
                        ].join(' ')}
                      />
                    </td>
                    <td className="py-1.5 pr-2">
                      <input
                        value={row.label}
                        onChange={e => update(i, { label: e.target.value })}
                        placeholder="特休"
                        className="w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
                      />
                    </td>
                    <td className="py-1.5 text-right">
                      <button
                        type="button"
                        onClick={() => remove(i)}
                        className="flex h-6 w-6 items-center justify-center rounded text-ink-faint hover:bg-danger/10 hover:text-danger"
                        title="移除"
                      ><Trash2 className="h-3 w-3" /></button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}

        <button
          type="button"
          onClick={add}
          className="mt-3 inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary"
        >
          <Plus className="h-3 w-3" /> 加選項
        </button>
      </div>
    </Modal>
  )
}
