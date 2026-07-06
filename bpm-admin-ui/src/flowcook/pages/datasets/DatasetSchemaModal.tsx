/**
 * Create / edit a dataset's schema (key, name, description, columns).
 * Rows are edited inline on DatasetsPage — this modal only owns the
 * shape. Backing API: POST /api/datasets, PUT /api/datasets/{id},
 * DELETE /api/datasets/{id} (SystemAdmin policy).
 */
import { useEffect, useState } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import { Modal } from '@/components/ui/modal'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  createDataset, updateDataset, deleteDataset,
  type DatasetDto, type DatasetColumnDef,
} from '@/flowcook/api/datasets'

const COLUMN_TYPES = ['text', 'number', 'date'] as const

interface Props {
  open: boolean
  onClose: () => void
  /** null = create mode; a dataset = edit its schema. */
  existing: DatasetDto | null
  onSaved: (ds: DatasetDto) => void
  onDeleted: (id: string) => void
}

export function DatasetSchemaModal({ open, onClose, existing, onSaved, onDeleted }: Props) {
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [columns, setColumns] = useState<DatasetColumnDef[]>([{ key: '', label: '', type: 'text' }])
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (!open) return
    setError(null)
    setSaving(false)
    setConfirmDelete(false)
    setKey(existing?.key ?? '')
    setName(existing?.name ?? '')
    setDescription(existing?.description ?? '')
    setColumns(existing?.columns.length ? existing.columns.map(c => ({ ...c })) : [{ key: '', label: '', type: 'text' }])
  }, [open, existing])

  function setCol(i: number, patch: Partial<DatasetColumnDef>) {
    setColumns(cols => cols.map((c, idx) => (idx === i ? { ...c, ...patch } : c)))
  }

  function validate(): string | null {
    if (!existing && !/^[a-z0-9][a-z0-9-_]*$/.test(key)) return 'Key 必填：小寫英數，可含 - 或 _（例：tw-districts）'
    if (!name.trim()) return '名稱必填'
    const defs = columns.filter(c => c.key.trim() || c.label.trim())
    if (defs.length === 0) return '至少需要一個欄位'
    for (const c of defs) {
      if (!/^[a-z0-9][a-z0-9_]*$/.test(c.key)) return `欄位 key「${c.key || '(空白)'}」需為小寫英數/底線（例：region_code）`
      if (!c.label.trim()) return `欄位「${c.key}」缺顯示名稱`
    }
    if (new Set(defs.map(c => c.key)).size !== defs.length) return '欄位 key 重複'
    return null
  }

  async function save() {
    const problem = validate()
    if (problem) { setError(problem); return }
    const defs = columns.filter(c => c.key.trim() || c.label.trim())
    setSaving(true)
    setError(null)
    try {
      const saved = existing
        ? await updateDataset(existing.id, { name: name.trim(), description: description.trim() || null, columns: defs })
        : await createDataset({ key: key.trim(), name: name.trim(), description: description.trim() || null, columns: defs })
      onSaved(saved)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  async function remove() {
    if (!existing) return
    setSaving(true)
    try {
      await deleteDataset(existing.id)
      onDeleted(existing.id)
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setConfirmDelete(false)
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        title={existing ? `編輯資料集 — ${existing.name}` : '新增資料集'}
        size="md"
        footer={
          <div className="flex w-full items-center justify-between">
            <div>
              {existing && (
                <Button variant="ghost" size="sm" className="text-danger hover:bg-red-50"
                  onClick={() => setConfirmDelete(true)} disabled={saving} data-testid="dataset-delete">
                  <Trash2 className="h-4 w-4" /> 刪除資料集
                </Button>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Button variant="ghost" onClick={onClose} disabled={saving}>取消</Button>
              <Button variant="primary" onClick={save} disabled={saving} data-testid="dataset-save">
                {saving ? '儲存中…' : existing ? '儲存變更' : '建立資料集'}
              </Button>
            </div>
          </div>
        }>
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <label className="block text-sm">
              <span className="mb-1 block font-medium text-ink">Key <span className="text-danger">*</span></span>
              <Input value={key} onChange={e => setKey(e.target.value)} placeholder="例：tw-districts"
                disabled={!!existing} data-testid="dataset-key" />
              <span className="mt-1 block text-xs text-ink-faint">
                {existing ? '建立後不可改（OData 表名 / 表單綁定都用它）' : '小寫英數，可含 - 或 _；OData 動態表與表單綁定都用這個 id'}
              </span>
            </label>
            <label className="block text-sm">
              <span className="mb-1 block font-medium text-ink">名稱 <span className="text-danger">*</span></span>
              <Input value={name} onChange={e => setName(e.target.value)} placeholder="例：台灣行政區劃" data-testid="dataset-name" />
            </label>
          </div>
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-ink">描述</span>
            <Input value={description} onChange={e => setDescription(e.target.value)} placeholder="選填" data-testid="dataset-desc" />
          </label>

          <div>
            <div className="mb-1 flex items-center justify-between">
              <span className="text-sm font-medium text-ink">欄位 <span className="text-danger">*</span></span>
              <Button variant="outline" size="xs"
                onClick={() => setColumns(cols => [...cols, { key: '', label: '', type: 'text' }])}
                data-testid="dataset-add-col">
                <Plus className="h-3.5 w-3.5" /> 加欄位
              </Button>
            </div>
            {existing && (
              <p className="mb-2 text-xs text-amber-700">
                改欄位不會搬移既有資料列：移除的欄位其資料仍留在列上但不再顯示；新欄位在舊列上是空值。
              </p>
            )}
            <div className="space-y-2">
              {columns.map((c, i) => (
                <div key={i} className="flex items-center gap-2">
                  <Input className="flex-1" value={c.key} placeholder="key（例：region）"
                    onChange={e => setCol(i, { key: e.target.value })} data-testid={`col-key-${i}`} />
                  <Input className="flex-1" value={c.label} placeholder="顯示名稱（例：區域）"
                    onChange={e => setCol(i, { label: e.target.value })} data-testid={`col-label-${i}`} />
                  <select
                    className="h-9 rounded-md border border-rule bg-white px-2 text-sm text-ink"
                    value={c.type}
                    onChange={e => setCol(i, { type: e.target.value })}
                    data-testid={`col-type-${i}`}>
                    {COLUMN_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
                  </select>
                  <Button variant="ghost" size="icon" title="移除欄位"
                    onClick={() => setColumns(cols => cols.filter((_, idx) => idx !== i))}
                    disabled={columns.length <= 1}
                    className="text-danger hover:bg-red-50" data-testid={`col-del-${i}`}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              ))}
            </div>
          </div>

          {error && <p className="text-sm text-danger" data-testid="dataset-error">{error}</p>}
        </div>
      </Modal>

      <ConfirmDialog
        open={confirmDelete}
        title="Delete dataset?"
        titleZh={`刪除資料集「${existing?.name ?? ''}」？`}
        description="資料集與其全部資料列會一併刪除；引用它的表單下拉會取不到值。此動作無法復原。"
        confirmText="刪除"
        tone="danger"
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </>
  )
}
