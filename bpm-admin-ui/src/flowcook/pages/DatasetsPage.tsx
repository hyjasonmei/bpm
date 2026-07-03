import { useEffect, useMemo, useRef, useState } from 'react'
import {
  useReactTable, getCoreRowModel, flexRender, type ColumnDef,
} from '@tanstack/react-table'
import { Plus, Trash2, Pencil, Check, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import {
  listDatasets, listRows, addRow, updateRow, deleteRow,
  type DatasetDto, type DatasetRowDto,
} from '@/flowcook/api/datasets'

export default function DatasetsPage() {
  const [datasets, setDatasets] = useState<DatasetDto[]>([])
  const [selected, setSelected] = useState<DatasetDto | null>(null)
  const [rows, setRows] = useState<DatasetRowDto[]>([])

  // Row edit-mode: at most one row editable at a time. In-flight cell values live
  // in a ref (not state) so a keystroke neither re-renders the table nor rebuilds
  // the memoized columns — that would remount the inputs (dropping characters) and
  // capture a stale `saveEdit` closure. `newRowId` marks a freshly-added blank row
  // so 取消 removes it instead of leaving an empty row.
  const [editingRowId, setEditingRowId] = useState<string | null>(null)
  const [newRowId, setNewRowId] = useState<string | null>(null)
  const draftRef = useRef<Record<string, string>>({})

  useEffect(() => { listDatasets().then(setDatasets) }, [])
  useEffect(() => {
    if (selected) listRows(selected.id).then(setRows); else setRows([])
    setEditingRowId(null); setNewRowId(null); draftRef.current = {}
  }, [selected])

  function startEdit(row: DatasetRowDto) {
    draftRef.current = { ...row.cells }
    setNewRowId(null)
    setEditingRowId(row.id)
  }
  function cancelEdit() {
    // Discard a never-saved new row entirely; otherwise just leave edit mode.
    if (newRowId && editingRowId === newRowId) {
      deleteRow(newRowId).catch(() => {})
      setRows(rs => rs.filter(r => r.id !== newRowId))
    }
    draftRef.current = {}
    setEditingRowId(null); setNewRowId(null)
  }
  async function saveEdit() {
    if (!editingRowId) return
    const saved = await updateRow(editingRowId, { cells: draftRef.current })
    setRows(rs => rs.map(r => r.id === saved.id ? saved : r))
    draftRef.current = {}
    setEditingRowId(null); setNewRowId(null)
  }
  async function toggleActive(row: DatasetRowDto) {
    const saved = await updateRow(row.id, { isActive: !row.isActive })
    setRows(rs => rs.map(r => r.id === saved.id ? saved : r))
  }
  async function removeRow(row: DatasetRowDto) {
    await deleteRow(row.id); setRows(rs => rs.filter(r => r.id !== row.id))
  }
  async function newRow() {
    if (!selected) return
    const blank = Object.fromEntries(selected.columns.map(c => [c.key, '']))
    const created = await addRow(selected.id, { cells: blank })
    setRows(rs => [...rs, created])
    draftRef.current = { ...blank }
    setNewRowId(created.id); setEditingRowId(created.id)
  }

  const columns = useMemo<ColumnDef<DatasetRowDto>[]>(() => {
    const cols: ColumnDef<DatasetRowDto>[] = (selected?.columns ?? []).map(c => ({
      id: c.key, header: c.label,
      cell: ({ row }) => editingRowId === row.original.id
        ? (
          // Uncontrolled (defaultValue) so a keystroke updates `draft` WITHOUT
          // rebuilding the column defs — a controlled value here would remount the
          // input every keypress (draft in the useMemo deps) and drop characters.
          <Input
            defaultValue={row.original.cells[c.key] ?? ''}
            onChange={e => { draftRef.current[c.key] = e.target.value }}
            data-testid={`cell-${c.key}-${row.index}`}
          />
        )
        : (
          <span className="text-ink" data-testid={`cell-${c.key}-${row.index}`}>
            {row.original.cells[c.key] || <span className="text-ink-faint">—</span>}
          </span>
        ),
    }))
    cols.push({
      id: '_actions', header: '',
      cell: ({ row }) => editingRowId === row.original.id
        ? (
          <div className="flex items-center justify-end gap-1">
            <Button variant="primary" size="xs" onClick={saveEdit} data-testid={`save-${row.index}`}>
              <Check className="h-3.5 w-3.5" /> 儲存
            </Button>
            <Button variant="ghost" size="xs" onClick={cancelEdit} data-testid={`cancel-${row.index}`}>
              <X className="h-3.5 w-3.5" /> 取消
            </Button>
          </div>
        )
        : (
          <div className="flex items-center justify-end gap-2">
            <Button variant="outline" size="xs" onClick={() => startEdit(row.original)}
              disabled={editingRowId !== null} data-testid={`edit-${row.index}`}>
              <Pencil className="h-3.5 w-3.5" /> 編輯
            </Button>
            <Switch
              checked={row.original.isActive}
              onCheckedChange={() => toggleActive(row.original)}
              disabled={editingRowId !== null}
              aria-label={row.original.isActive ? '停用' : '啟用'}
              title={row.original.isActive ? '停用' : '啟用'}
              data-testid={`toggle-${row.index}`}
            />
            <Button variant="ghost" size="icon" onClick={() => removeRow(row.original)}
              disabled={editingRowId !== null} title="刪除"
              className="text-danger hover:bg-red-50" data-testid={`del-${row.index}`}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ),
    })
    return cols
    // Rebuild only when the edit target changes — cell drafts live in draftRef so
    // keystrokes never touch this memo (no per-key remount, no stale saveEdit).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected, rows, editingRowId])

  const table = useReactTable({ data: rows, columns, getCoreRowModel: getCoreRowModel() })

  return (
    <div className="grid grid-cols-12 gap-4 p-4">
      <aside className="col-span-3 space-y-1">
        <h2 className="mb-2 text-sm font-semibold text-ink">資料集 / Datasets</h2>
        {datasets.map(d => (
          <button key={d.id} onClick={() => setSelected(d)} data-testid={`dataset-${d.key}`}
            className={`block w-full rounded px-3 py-2 text-left text-sm ${selected?.id === d.id ? 'bg-primary/10 text-ink' : 'text-ink-muted hover:bg-slate-50'}`}>
            {d.name} <span className="text-ink-faint">· {d.rowCount}</span>
          </button>
        ))}
      </aside>

      <section className="col-span-9">
        {!selected ? (
          <p className="text-sm text-ink-muted">選擇一個資料集以編輯內容。</p>
        ) : (
          <>
            <div className="mb-3 flex items-center justify-between">
              <h3 className="text-sm font-semibold text-ink">{selected.name}</h3>
              <Button variant="primary" size="sm" onClick={newRow}
                disabled={editingRowId !== null} data-testid="add-row">
                <Plus className="h-4 w-4" /> 新增列
              </Button>
            </div>
            <table className="w-full border-collapse text-sm">
              <thead>
                {table.getHeaderGroups().map(hg => (
                  <tr key={hg.id} className="border-b border-rule text-left text-ink-muted">
                    {hg.headers.map(h => <th key={h.id} className="py-2 pr-3 font-medium">
                      {flexRender(h.column.columnDef.header, h.getContext())}</th>)}
                  </tr>
                ))}
              </thead>
              <tbody>
                {table.getRowModel().rows.map(r => (
                  <tr key={r.id} className={`border-b border-rule ${r.original.isActive ? '' : 'opacity-40'}`}>
                    {r.getVisibleCells().map(cell => <td key={cell.id} className="py-1.5 pr-3">
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}</td>)}
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </section>
    </div>
  )
}
