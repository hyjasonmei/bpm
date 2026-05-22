/**
 * FormPreviewModal — render the current draft form's fields as actual
 * HTML inputs so the user can see what the end-user will fill in. v0
 * is static: no submit / no validator wiring, no CEL evaluation, no
 * persona switching. Conditional fields are shown with a small note
 * instead of being hidden, so the author can review them all.
 *
 *   <FormPreviewModal open={open} task={task} onClose={...} />
 */
import { Modal } from '@/components/ui/modal'
import { Button } from '@/components/ui/button'
import type { FormField, UserTask } from '@/lib/onboarding'

interface Props {
  open: boolean
  task: UserTask
  taskLabel: string
  onClose: () => void
}

export function FormPreviewModal({ open, task, taskLabel, onClose }: Props) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={
        <span className="flex items-baseline gap-2">
          預覽
          <span className="text-sm font-medium text-ink-muted">{taskLabel}</span>
          <span className="font-mono text-[11px] text-ink-faint">{task.formCode}</span>
        </span>
      }
      size="lg"
      footer={<Button variant="primary" onClick={onClose}>Close</Button>}
    >
      <div className="space-y-3 bg-slate-50 p-6">
        <div className="rounded-md border border-rule bg-white p-5">
          <h3 className="mb-4 border-b border-rule pb-2 text-sm font-semibold text-ink">{taskLabel}</h3>
          {task.fields.length === 0 ? (
            <p className="py-6 text-center text-sm text-ink-faint">這張表單還沒有欄位。</p>
          ) : (
            <div className="space-y-4">
              {task.fields.map(f => <FieldPreview key={f.id} field={f} />)}
              <div className="border-t border-rule pt-3">
                <button
                  type="button"
                  disabled
                  className="rounded bg-primary px-4 py-1.5 text-sm font-semibold text-white opacity-50"
                  title="預覽僅顯示樣式，無實際送出"
                >
                  送出（預覽不可點）
                </button>
              </div>
            </div>
          )}
        </div>
        <p className="text-[11px] text-ink-faint">
          預覽僅為靜態樣式。Conditional / Validator / Derived 表達式不會在這裡求值；改去 chef 產生的 sandbox 才會跑真實邏輯。
        </p>
      </div>
    </Modal>
  )
}

function FieldPreview({ field }: { field: FormField }) {
  const label = field.label['zh-TW'] || field.id
  const helperBits: string[] = []
  if (field.conditional !== undefined) helperBits.push('⚠ 依條件顯示')
  if (field.validator !== undefined) helperBits.push('✓ 有驗證規則')
  if (field.type === 'derived') helperBits.push('Σ 計算欄位（不可手填）')

  return (
    <div className="space-y-1">
      <label className="block text-xs font-medium text-ink">
        {label}
        {field.required && <span className="ml-1 text-danger">*</span>}
      </label>
      <PreviewInput field={field} />
      {helperBits.length > 0 && (
        <p className="font-mono text-[10px] text-ink-faint">{helperBits.join(' · ')}</p>
      )}
      {field.note?.['zh-TW'] && (
        <p className="text-[10px] italic text-ink-muted">📝 {field.note['zh-TW']}</p>
      )}
    </div>
  )
}

function PreviewInput({ field }: { field: FormField }) {
  const baseCls = 'block w-full rounded border border-rule bg-white px-3 py-1.5 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20'
  switch (field.type) {
    case 'text':
      return <input type="text" className={baseCls} placeholder={`(${field.id})`} />
    case 'textarea':
      return <textarea rows={3} className={baseCls} placeholder={`(${field.id})`} />
    case 'number':
      return <input type="number" className={baseCls} placeholder="0" />
    case 'date':
      return <input type="date" className={baseCls} />
    case 'daterange':
      return (
        <div className="flex items-center gap-2">
          <input type="date" className={baseCls} />
          <span className="text-ink-faint">→</span>
          <input type="date" className={baseCls} />
        </div>
      )
    case 'select':
      return (
        <select className={baseCls} defaultValue="">
          <option value="" disabled>— 選擇 —</option>
          {(field.options ?? []).map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )
    case 'multiselect':
      return (
        <div className="flex flex-wrap gap-3 rounded border border-rule bg-white p-2">
          {(field.options ?? []).length === 0 && <span className="text-xs text-ink-faint">尚未設選項</span>}
          {(field.options ?? []).map(o => (
            <label key={o.value} className="flex items-center gap-1.5 text-xs text-ink">
              <input type="checkbox" />
              {o.label}
            </label>
          ))}
        </div>
      )
    case 'file':
      return <input type="file" className="text-xs text-ink-muted" disabled />
    case 'user_picker':
      return (
        <div className={baseCls + ' flex items-center gap-2 text-ink-muted'}>
          <span className="text-xs">點此選 user（principal picker）</span>
        </div>
      )
    case 'derived':
      return (
        <div className={baseCls + ' bg-slate-100 italic text-ink-muted'}>
          <span className="text-xs">{field.derivedFrom ? `= ${field.derivedFrom}` : '（尚未設計算公式）'}</span>
        </div>
      )
    default:
      return null
  }
}
