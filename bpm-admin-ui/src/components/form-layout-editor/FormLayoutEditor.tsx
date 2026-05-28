import { useEffect } from 'react'
import { AlertTriangle, ChevronDown, Info, LayoutList, Plus, Repeat, ShieldAlert, Sigma, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import { DragGrip, mergeDragStyle, SortableList, type SortableHandleProps } from '@/components/sortable-list/SortableList'
import {
  buildDefaultLayout,
  collectLayoutFieldIds,
  newFieldUid,
  type BannerSeverity,
  type FieldColSpan,
  type FieldRef,
  type FieldType,
  type FormBanner,
  type FormField,
  type FormRepeater,
  type FormRow,
  type FormSection,
  type FormTotal,
  type LayoutChild,
  type UserTask,
} from '@/lib/onboarding'

interface Props {
  task: UserTask
  onChange: (next: UserTask) => void
}

const COLSPAN_OPTIONS: FieldColSpan[] = [3, 4, 6, 8, 12]

/**
 * Tier 1 layout authoring — sections + rows + banners over the task's
 * flat fields list. The tree is intentionally shallow (max 2 levels):
 * sections at the root, then rows / fieldRefs / banners inside each
 * section. Nested sections are not exposed in the UI even though the
 * schema allows them.
 */
export function FormLayoutEditor({ task, onChange }: Props) {
  // Bootstrap a default layout the first time the user lands here.
  useEffect(() => {
    if (!task.layout && task.fields.length > 0) {
      onChange({ ...task, layout: buildDefaultLayout(task.fields) })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const layout: LayoutChild[] = task.layout ?? []
  const placedIds = new Set(collectLayoutFieldIds(layout))
  const unplaced = task.fields.filter(f => !placedIds.has(f.id))

  const sections: FormSection[] = layout.filter(isSection)
  const looseChildren = layout.filter(c => !isSection(c)) // fallback if root has bare children

  function updateLayout(next: LayoutChild[]) {
    onChange({ ...task, layout: next })
  }

  function addSection() {
    const id = `section_${Date.now().toString(36).slice(-4)}`
    updateLayout([
      ...layout,
      { kind: 'section', id, title: { 'zh-TW': '新區塊' }, children: [] },
    ])
  }

  function updateSection(sectionIdx: number, patch: Partial<FormSection>) {
    const next = layout.map((c, i) =>
      i === sectionIdx && c.kind === 'section' ? { ...c, ...patch } : c
    )
    updateLayout(next)
  }

  function removeSection(sectionIdx: number) {
    if (!confirm('移除此區塊？裡面的欄位會回到「未放置」清單。')) return
    updateLayout(layout.filter((_, i) => i !== sectionIdx))
  }

  function setSectionChildren(sectionIdx: number, children: LayoutChild[]) {
    updateSection(sectionIdx, { children })
  }

  if (task.fields.length === 0) {
    return (
      <div className="rounded border border-dashed border-rule p-6 text-center text-xs text-ink-faint">
        先在上方加欄位，再來這裡排版。
      </div>
    )
  }

  return (
    <div className="space-y-3">
      {/* Sections */}
      {sections.length === 0 && looseChildren.length === 0 && (
        <div className="rounded border border-dashed border-rule p-4 text-center text-xs text-ink-faint">
          還沒有區塊。下方點「+ 新區塊」開始排版。
        </div>
      )}
      <SortableList
        items={sections}
        getId={s => s.id}
        onReorder={next => updateLayout([...next, ...looseChildren])}
      >
        {(section, _idx, handle) => {
          const layoutIdx = layout.indexOf(section)
          return (
            <SectionCard
              section={section}
              dragHandle={handle}
              fields={task.fields}
              unplaced={unplaced}
              onSectionPatch={p => updateSection(layoutIdx, p)}
              onRemoveSection={() => removeSection(layoutIdx)}
              onChildrenChange={children => setSectionChildren(layoutIdx, children)}
            />
          )
        }}
      </SortableList>

      {/* Add section button */}
      <button
        type="button"
        onClick={addSection}
        className="flex w-full items-center justify-center gap-1.5 rounded border border-dashed border-rule px-3 py-2 text-xs font-medium text-ink-muted transition-colors hover:border-primary hover:text-primary"
      >
        <Plus className="h-3.5 w-3.5" />
        新區塊
      </button>

      {/* Unplaced fields */}
      {unplaced.length > 0 && (
        <div className="rounded border border-dashed border-amber-300 bg-amber-50/40 p-3">
          <div className="mb-2 flex items-center gap-1.5 text-[11px] font-semibold text-amber-700">
            <AlertTriangle className="h-3 w-3" />
            未放置欄位（{unplaced.length}） — chef 會 fallback 用 flat 一列
          </div>
          <div className="flex flex-wrap gap-1.5">
            {unplaced.map(f => (
              <span
                key={f.id}
                className="inline-flex items-center gap-1 rounded border border-amber-300 bg-white px-2 py-0.5 font-mono text-[10px] text-ink-muted"
              >
                {f.label['zh-TW'] ?? f.id}
              </span>
            ))}
          </div>
          <p className="mt-2 text-[10px] text-amber-700">
            點任一區塊內的「+ 加欄位」把它們放進去。
          </p>
        </div>
      )}
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────
// Section card — contains rows / fieldRefs / banners
// ─────────────────────────────────────────────────────────────────────

function SectionCard({
  section, fields, unplaced, onSectionPatch, onRemoveSection, onChildrenChange, dragHandle,
}: {
  section: FormSection
  fields: FormField[]
  unplaced: FormField[]
  onSectionPatch: (p: Partial<FormSection>) => void
  onRemoveSection: () => void
  onChildrenChange: (children: LayoutChild[]) => void
  dragHandle?: SortableHandleProps
}) {
  const children = section.children

  function addFieldRef(fieldId: string) {
    onChildrenChange([...children, { kind: 'fieldRef', id: fieldId }])
  }
  function addRow() {
    onChildrenChange([
      ...children,
      { kind: 'row', id: `row_${Date.now().toString(36).slice(-4)}`, children: [] },
    ])
  }
  function addBanner() {
    onChildrenChange([
      ...children,
      {
        kind: 'banner',
        id: `banner_${Date.now().toString(36).slice(-4)}`,
        severity: 'info',
        text: { 'zh-TW': '提示文字' },
      },
    ])
  }
  function addRepeater() {
    onChildrenChange([
      ...children,
      {
        kind: 'repeater',
        id: `rep_${Date.now().toString(36).slice(-4)}`,
        title: { 'zh-TW': '新多筆群組' },
        minCount: 1,
        itemFields: [],
        itemLayout: [],
        totals: [],
      },
    ])
  }
  function updateChild(idx: number, patch: Partial<LayoutChild>) {
    const next = children.map((c, i) => i === idx ? ({ ...c, ...patch } as LayoutChild) : c)
    onChildrenChange(next)
  }
  function removeChild(idx: number) {
    onChildrenChange(children.filter((_, i) => i !== idx))
  }

  return (
    <div
      ref={dragHandle?.setNodeRef}
      style={mergeDragStyle(dragHandle)}
      className={cn(
        'rounded-md border border-rule bg-card',
        dragHandle?.isDragging && 'opacity-60 shadow-lg',
      )}
    >
      {/* Section header */}
      <div className="flex items-center gap-2 border-b border-rule bg-slate-50/60 px-3 py-2">
        <DragGrip handle={dragHandle} iconClassName="h-3.5 w-3.5" />
        <input
          value={section.title['zh-TW']}
          onChange={e => onSectionPatch({ title: { ...section.title, 'zh-TW': e.target.value } })}
          placeholder="區塊標題"
          className="flex-1 rounded border border-transparent bg-transparent px-1.5 py-0.5 text-sm font-semibold text-ink outline-none hover:bg-white focus:border-primary focus:bg-white"
        />
        <ConditionInput
          value={section.condition}
          onChange={c => onSectionPatch({ condition: c || undefined })}
        />
        <button
          type="button"
          onClick={onRemoveSection}
          title="移除區塊"
          className="text-ink-faint transition-colors hover:text-danger"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Children */}
      <div className="space-y-2 p-3">
        {children.length === 0 && (
          <div className="rounded border border-dashed border-rule p-3 text-center text-[11px] text-ink-faint">
            區塊還沒內容。下方按鈕加欄位 / 多欄列 / 提示。
          </div>
        )}
        <SortableList
          items={children}
          getId={(c, idx) => `${c.kind}-${'id' in c ? c.id : idx}-${idx}`}
          onReorder={onChildrenChange}
        >
          {(child, idx, handle) => {
            if (child.kind === 'fieldRef') {
              const fld = fields.find(f => f.id === child.id)
              return (
                <FieldRefRow
                  fieldRef={child}
                  field={fld}
                  dragHandle={handle}
                  onPatch={p => updateChild(idx, p as Partial<FieldRef>)}
                  onRemove={() => removeChild(idx)}
                />
              )
            }
            if (child.kind === 'row') {
              return (
                <RowCard
                  row={child}
                  fields={fields}
                  unplaced={unplaced}
                  dragHandle={handle}
                  onPatch={p => updateChild(idx, p as Partial<FormRow>)}
                  onRemove={() => removeChild(idx)}
                />
              )
            }
            if (child.kind === 'banner') {
              return (
                <BannerCard
                  banner={child}
                  dragHandle={handle}
                  onPatch={p => updateChild(idx, p as Partial<FormBanner>)}
                  onRemove={() => removeChild(idx)}
                />
              )
            }
            if (child.kind === 'repeater') {
              return (
                <RepeaterCard
                  repeater={child}
                  dragHandle={handle}
                  onPatch={p => updateChild(idx, p as Partial<FormRepeater>)}
                  onRemove={() => removeChild(idx)}
                />
              )
            }
            // Defensive: nested section inside a section. Schema permits
            // it but the UI doesn't expose nesting controls; render a
            // disabled card so existing data isn't lost.
            if (child.kind === 'section') {
              return (
                <div
                  ref={handle.setNodeRef}
                  style={mergeDragStyle(handle)}
                  className="rounded border border-dashed border-rule p-2 text-[11px] text-ink-muted"
                >
                  嵌套區塊「{child.title['zh-TW']}」— 此 UI 不支援編輯，請改成 row / banner。
                </div>
              )
            }
            return null
          }}
        </SortableList>
      </div>

      {/* Footer add buttons */}
      <div className="flex items-center gap-2 border-t border-rule bg-slate-50/60 px-3 py-1.5">
        <UnplacedFieldPicker
          options={unplaced}
          onPick={addFieldRef}
        />
        <button
          type="button"
          onClick={addRow}
          className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[11px] text-ink-muted transition-colors hover:border-primary hover:text-primary"
        >
          <Plus className="h-3 w-3" /> 多欄列
        </button>
        <button
          type="button"
          onClick={addBanner}
          className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[11px] text-ink-muted transition-colors hover:border-primary hover:text-primary"
        >
          <Plus className="h-3 w-3" /> 提示文字
        </button>
        <button
          type="button"
          onClick={addRepeater}
          className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[11px] text-ink-muted transition-colors hover:border-primary hover:text-primary"
        >
          <Repeat className="h-3 w-3" /> 多筆 repeater
        </button>
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────
// Sub-cards
// ─────────────────────────────────────────────────────────────────────

function FieldRefRow({ fieldRef, field, onPatch, onRemove, dragHandle }: {
  fieldRef: FieldRef
  field: FormField | undefined
  onPatch: (p: Partial<FieldRef>) => void
  onRemove: () => void
  dragHandle?: SortableHandleProps
}) {
  const missing = !field
  return (
    <div
      ref={dragHandle?.setNodeRef}
      style={mergeDragStyle(dragHandle)}
      className={cn(
        'flex items-center gap-2 rounded border px-2.5 py-1.5',
        missing ? 'border-danger/30 bg-danger/5' : 'border-rule bg-white',
        dragHandle?.isDragging && 'opacity-60 shadow-md',
      )}
    >
      <DragGrip handle={dragHandle} />
      <div className="flex-1 min-w-0">
        <div className="text-xs text-ink truncate">
          {missing
            ? <span className="text-danger">⚠ 欄位 {fieldRef.id} 已被刪除</span>
            : field.label['zh-TW']}
        </div>
        <div className="font-mono text-[10px] text-ink-faint">
          {field?.type ?? ''} · {fieldRef.id}
        </div>
      </div>
      <ColSpanSelect value={fieldRef.colSpan ?? 12} onChange={n => onPatch({ colSpan: n })} />
      <button onClick={onRemove} title="從區塊移除" className="text-ink-faint hover:text-danger">
        <Trash2 className="h-3 w-3" />
      </button>
    </div>
  )
}

function RowCard({ row, fields, unplaced, onPatch, onRemove, dragHandle }: {
  row: FormRow
  fields: FormField[]
  unplaced: FormField[]
  onPatch: (p: Partial<FormRow>) => void
  onRemove: () => void
  dragHandle?: SortableHandleProps
}) {
  function addCell(fieldId: string) {
    onPatch({ children: [...row.children, { kind: 'fieldRef', id: fieldId, colSpan: 6 }] })
  }
  function updateCell(idx: number, patch: Partial<FieldRef>) {
    onPatch({ children: row.children.map((c, i) => i === idx ? { ...c, ...patch } : c) })
  }
  function removeCell(idx: number) {
    onPatch({ children: row.children.filter((_, i) => i !== idx) })
  }
  const total = row.children.reduce((sum, c) => sum + (c.colSpan ?? 12), 0)
  const balanced = total === 12
  return (
    <div
      ref={dragHandle?.setNodeRef}
      style={mergeDragStyle(dragHandle)}
      className={cn(
        'rounded border border-rule bg-slate-50/40',
        dragHandle?.isDragging && 'opacity-60 shadow-md',
      )}
    >
      <div className="flex items-center gap-2 border-b border-rule px-2.5 py-1.5">
        <DragGrip handle={dragHandle} />
        <span className="font-mono text-[10px] uppercase tracking-[0.14em] text-ink-muted">
          多欄列
        </span>
        <span className={cn(
          'font-mono text-[10px]',
          balanced ? 'text-good' : 'text-warn',
        )}>
          {total}/12
        </span>
        <span className="flex-1" />
        <ConditionInput value={row.condition} onChange={c => onPatch({ condition: c || undefined })} />
        <button onClick={onRemove} title="移除多欄列" className="text-ink-faint hover:text-danger">
          <Trash2 className="h-3 w-3" />
        </button>
      </div>
      <div className="space-y-1.5 p-2">
        {row.children.length === 0 && (
          <div className="rounded border border-dashed border-rule p-2 text-center text-[10px] text-ink-faint">
            下方加欄位變成多欄並列
          </div>
        )}
        <SortableList
          items={row.children}
          getId={(c, idx) => `${c.id}-${idx}`}
          onReorder={next => onPatch({ children: next })}
        >
          {(cell, idx, handle) => {
            const fld = fields.find(f => f.id === cell.id)
            return (
              <div
                ref={handle.setNodeRef}
                style={mergeDragStyle(handle)}
                className={cn(
                  'flex items-center gap-2 rounded border bg-white px-2.5 py-1.5',
                  fld ? 'border-rule' : 'border-danger/30 bg-danger/5',
                  handle.isDragging && 'opacity-60 shadow-md',
                )}
              >
                <DragGrip handle={handle} />
                <div className="flex-1 min-w-0">
                  <div className="text-xs text-ink truncate">
                    {fld?.label['zh-TW'] ?? <span className="text-danger">⚠ {cell.id}</span>}
                  </div>
                  <div className="font-mono text-[10px] text-ink-faint">{cell.id}</div>
                </div>
                <ColSpanSelect value={cell.colSpan ?? 6} onChange={n => updateCell(idx, { colSpan: n })} />
                <button onClick={() => removeCell(idx)} title="從列中移除" className="text-ink-faint hover:text-danger">
                  <Trash2 className="h-3 w-3" />
                </button>
              </div>
            )
          }}
        </SortableList>
        <UnplacedFieldPicker options={unplaced} onPick={addCell} compact />
      </div>
    </div>
  )
}

function BannerCard({ banner, onPatch, onRemove, dragHandle }: {
  banner: FormBanner
  onPatch: (p: Partial<FormBanner>) => void
  onRemove: () => void
  dragHandle?: SortableHandleProps
}) {
  const tone = SEVERITY_TONE[banner.severity]
  return (
    <div
      ref={dragHandle?.setNodeRef}
      style={mergeDragStyle(dragHandle)}
      className={cn(
        'rounded border px-2.5 py-2',
        tone.border,
        tone.bg,
        dragHandle?.isDragging && 'opacity-60 shadow-md',
      )}
    >
      <div className="mb-1 flex items-center gap-2">
        <DragGrip handle={dragHandle} />
        {tone.icon}
        <select
          value={banner.severity}
          onChange={e => onPatch({ severity: e.target.value as BannerSeverity })}
          className="rounded border border-transparent bg-transparent px-1 py-0.5 text-[10px] font-mono uppercase tracking-[0.14em] text-ink-muted outline-none hover:bg-white focus:border-rule focus:bg-white"
        >
          <option value="info">info</option>
          <option value="warn">warn</option>
          <option value="danger">danger</option>
        </select>
        <span className="flex-1" />
        <ConditionInput value={banner.condition} onChange={c => onPatch({ condition: c || undefined })} />
        <button onClick={onRemove} title="移除提示" className="text-ink-faint hover:text-danger">
          <Trash2 className="h-3 w-3" />
        </button>
      </div>
      <textarea
        value={banner.text['zh-TW']}
        onChange={e => onPatch({ text: { ...banner.text, 'zh-TW': e.target.value } })}
        rows={2}
        placeholder="提示文字（顯示給填單人）"
        className="block w-full resize-y rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none placeholder:text-ink-faint focus:border-primary"
      />
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────
// Small reusable bits
// ─────────────────────────────────────────────────────────────────────

function ColSpanSelect({ value, onChange }: { value: FieldColSpan; onChange: (n: FieldColSpan) => void }) {
  return (
    <select
      value={value}
      onChange={e => onChange(Number(e.target.value) as FieldColSpan)}
      title="欄寬（12 = 全寬）"
      className="rounded border border-rule bg-white px-1 py-0.5 font-mono text-[10px] text-ink-muted outline-none hover:border-primary focus:border-primary"
    >
      {COLSPAN_OPTIONS.map(n => (
        <option key={n} value={n}>{n}/12</option>
      ))}
    </select>
  )
}

function ConditionInput({ value, onChange }: { value: string | undefined; onChange: (v: string) => void }) {
  return (
    <input
      value={value ?? ''}
      onChange={e => onChange(e.target.value)}
      placeholder="CEL 顯示條件（可空）"
      title="留空 = 永遠顯示。寫 CEL，譬如 amount > 50000"
      className="w-44 rounded border border-rule bg-white px-1.5 py-0.5 font-mono text-[10px] text-ink-muted outline-none placeholder:text-ink-faint hover:border-primary focus:border-primary"
    />
  )
}

function UnplacedFieldPicker({ options, onPick, compact }: {
  options: FormField[]
  onPick: (fieldId: string) => void
  compact?: boolean
}) {
  if (options.length === 0) {
    return (
      <span className={cn('text-[10px] text-ink-faint', compact ? '' : 'inline-flex items-center gap-1')}>
        所有欄位都已放置
      </span>
    )
  }
  return (
    <div className="relative inline-block">
      <select
        defaultValue=""
        onChange={e => {
          const v = e.target.value
          if (v) onPick(v)
          e.target.value = '' // reset so re-picking same option triggers onChange
        }}
        className={cn(
          'cursor-pointer appearance-none rounded border border-rule bg-white pl-2 pr-6 text-[11px] text-ink-muted transition-colors hover:border-primary hover:text-primary',
          compact ? 'py-0.5' : 'py-1',
        )}
      >
        <option value="">＋ 加欄位 ({options.length})</option>
        {options.map(f => (
          <option key={f.id} value={f.id}>
            {f.label['zh-TW']} ({f.id})
          </option>
        ))}
      </select>
      <ChevronDown className="pointer-events-none absolute right-1 top-1/2 h-3 w-3 -translate-y-1/2 text-ink-faint" />
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────
// Repeater (Tier 2)
// ─────────────────────────────────────────────────────────────────────

const FIELD_TYPE_OPTIONS: FieldType[] = [
  'text', 'textarea', 'number', 'date', 'daterange',
  'select', 'multiselect', 'file', 'user_picker', 'derived',
]
const TOTAL_FORMATS: NonNullable<FormTotal['format']>[] = ['currency', 'number', 'percent']

function RepeaterCard({ repeater, onPatch, onRemove, dragHandle }: {
  repeater: FormRepeater
  onPatch: (p: Partial<FormRepeater>) => void
  onRemove: () => void
  dragHandle?: SortableHandleProps
}) {
  const { itemFields, itemLayout, totals = [] } = repeater
  const placedItemIds = new Set(collectItemFieldIds(itemLayout))
  const unplacedItem = itemFields.filter(f => !placedItemIds.has(f.id))

  function addItemField() {
    const id = `f_${Date.now().toString(36).slice(-4)}`
    const newField: FormField = { id, label: { 'zh-TW': '新欄位' }, type: 'text', required: false, _uid: newFieldUid() }
    const nextFields = [...itemFields, newField]
    // Auto-place it at the tail of the item layout so the row shows up
    // alongside the fields list.
    const nextLayout: LayoutChild[] = [...itemLayout, { kind: 'fieldRef', id }]
    onPatch({ itemFields: nextFields, itemLayout: nextLayout })
  }
  function updateItemField(idx: number, patch: Partial<FormField>) {
    onPatch({ itemFields: itemFields.map((f, i) => i === idx ? { ...f, ...patch } : f) })
  }
  function removeItemField(idx: number) {
    const removedId = itemFields[idx].id
    onPatch({
      itemFields: itemFields.filter((_, i) => i !== idx),
      // Strip any fieldRefs to the removed id from the layout tree.
      itemLayout: stripFieldRefs(itemLayout, removedId),
    })
  }

  function addItemRow() {
    onPatch({
      itemLayout: [
        ...itemLayout,
        { kind: 'row', id: `row_${Date.now().toString(36).slice(-4)}`, children: [] },
      ],
    })
  }
  function addItemBanner() {
    onPatch({
      itemLayout: [
        ...itemLayout,
        {
          kind: 'banner',
          id: `banner_${Date.now().toString(36).slice(-4)}`,
          severity: 'info',
          text: { 'zh-TW': '提示文字' },
        },
      ],
    })
  }
  function placeItemField(fieldId: string) {
    onPatch({ itemLayout: [...itemLayout, { kind: 'fieldRef', id: fieldId }] })
  }
  function updateItemChild(idx: number, patch: Partial<LayoutChild>) {
    onPatch({
      itemLayout: itemLayout.map((c, i) =>
        i === idx ? ({ ...c, ...patch } as LayoutChild) : c
      ),
    })
  }
  function removeItemChild(idx: number) {
    onPatch({ itemLayout: itemLayout.filter((_, i) => i !== idx) })
  }

  function addTotal() {
    onPatch({
      totals: [
        ...totals,
        {
          id: `total_${Date.now().toString(36).slice(-4)}`,
          label: { 'zh-TW': '合計' },
          formula: 'sum(items.amount)',
          format: 'currency',
        },
      ],
    })
  }
  function updateTotal(idx: number, patch: Partial<FormTotal>) {
    onPatch({ totals: totals.map((t, i) => i === idx ? { ...t, ...patch } : t) })
  }
  function removeTotal(idx: number) {
    onPatch({ totals: totals.filter((_, i) => i !== idx) })
  }

  return (
    <div
      ref={dragHandle?.setNodeRef}
      style={mergeDragStyle(dragHandle)}
      className={cn(
        'rounded-md border border-accent/30 bg-accent/5',
        dragHandle?.isDragging && 'opacity-60 shadow-md',
      )}
    >
      {/* Header */}
      <div className="flex flex-wrap items-center gap-2 border-b border-accent/30 bg-accent/10 px-3 py-2">
        <DragGrip handle={dragHandle} iconClassName="h-3.5 w-3.5 text-accent/70" />
        <Repeat className="h-3.5 w-3.5 shrink-0 text-accent" />
        <span className="font-mono text-[10px] uppercase tracking-[0.14em] text-accent">repeater</span>
        <input
          value={repeater.title['zh-TW']}
          onChange={e => onPatch({ title: { ...repeater.title, 'zh-TW': e.target.value } })}
          placeholder="多筆群組標題"
          className="flex-1 min-w-[140px] rounded border border-transparent bg-transparent px-1.5 py-0.5 text-sm font-semibold text-ink outline-none hover:bg-white focus:border-primary focus:bg-white"
        />
        <CountInput
          label="min"
          value={repeater.minCount}
          onChange={n => onPatch({ minCount: n })}
        />
        <CountInput
          label="max"
          value={repeater.maxCount}
          onChange={n => onPatch({ maxCount: n })}
        />
        <ConditionInput value={repeater.condition} onChange={c => onPatch({ condition: c || undefined })} />
        <button onClick={onRemove} title="移除 repeater" className="text-ink-faint hover:text-danger">
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Item fields */}
      <div className="border-b border-accent/30 p-3">
        <div className="mb-1.5 flex items-center gap-1.5">
          <LayoutList className="h-3 w-3 text-ink-muted" />
          <span className="text-[11px] font-semibold text-ink">每筆欄位</span>
          <button
            type="button"
            onClick={addItemField}
            className="ml-auto inline-flex items-center gap-1 rounded bg-primary px-1.5 py-0.5 text-[10px] font-semibold text-white hover:bg-blue-700"
          >
            <Plus className="h-2.5 w-2.5" /> 加欄位
          </button>
        </div>
        {itemFields.length === 0 ? (
          <div className="rounded border border-dashed border-rule p-2 text-center text-[10px] text-ink-faint">
            還沒有「每筆欄位」。點上方加欄位開始。
          </div>
        ) : (
          <div className="space-y-1.5">
            <SortableList
              items={itemFields}
              getId={(f, idx) => f._uid ?? `${f.id}-${idx}`}
              onReorder={next => onPatch({ itemFields: next })}
            >
              {(f, idx, handle) => (
                <ItemFieldRow
                  field={f}
                  dragHandle={handle}
                  onPatch={p => updateItemField(idx, p)}
                  onRemove={() => removeItemField(idx)}
                />
              )}
            </SortableList>
          </div>
        )}
      </div>

      {/* Item layout */}
      <div className="border-b border-accent/30 p-3">
        <div className="mb-1.5 flex items-center gap-1.5">
          <LayoutList className="h-3 w-3 text-ink-muted" />
          <span className="text-[11px] font-semibold text-ink">每筆排版</span>
        </div>
        {itemLayout.length === 0 ? (
          <div className="rounded border border-dashed border-rule p-2 text-center text-[10px] text-ink-faint">
            欄位會以 flat 一行渲染。下方按鈕可加多欄列 / 提示 / 指定排序。
          </div>
        ) : (
          <div className="space-y-1.5">
            <SortableList
              items={itemLayout}
              getId={(c, idx) => `${c.kind}-${'id' in c ? c.id : idx}-${idx}`}
              onReorder={next => onPatch({ itemLayout: next })}
            >
              {(child, idx, handle) => {
                if (child.kind === 'fieldRef') {
                  const fld = itemFields.find(f => f.id === child.id)
                  return (
                    <FieldRefRow
                      fieldRef={child}
                      field={fld}
                      dragHandle={handle}
                      onPatch={p => updateItemChild(idx, p as Partial<FieldRef>)}
                      onRemove={() => removeItemChild(idx)}
                    />
                  )
                }
                if (child.kind === 'row') {
                  return (
                    <RowCard
                      row={child}
                      fields={itemFields}
                      unplaced={unplacedItem}
                      dragHandle={handle}
                      onPatch={p => updateItemChild(idx, p as Partial<FormRow>)}
                      onRemove={() => removeItemChild(idx)}
                    />
                  )
                }
                if (child.kind === 'banner') {
                  return (
                    <BannerCard
                      banner={child}
                      dragHandle={handle}
                      onPatch={p => updateItemChild(idx, p as Partial<FormBanner>)}
                      onRemove={() => removeItemChild(idx)}
                    />
                  )
                }
                return (
                  <div
                    ref={handle.setNodeRef}
                    style={mergeDragStyle(handle)}
                    className="rounded border border-dashed border-rule p-2 text-[10px] text-ink-muted"
                  >
                    此 UI 不支援 repeater 內嵌 {child.kind} —— 請從 schema 改回。
                  </div>
                )
              }}
            </SortableList>
          </div>
        )}
        <div className="mt-2 flex items-center gap-2">
          <UnplacedFieldPicker options={unplacedItem} onPick={placeItemField} compact />
          <button
            type="button"
            onClick={addItemRow}
            className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[11px] text-ink-muted transition-colors hover:border-primary hover:text-primary"
          >
            <Plus className="h-3 w-3" /> 多欄列
          </button>
          <button
            type="button"
            onClick={addItemBanner}
            className="inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[11px] text-ink-muted transition-colors hover:border-primary hover:text-primary"
          >
            <Plus className="h-3 w-3" /> 提示文字
          </button>
        </div>
      </div>

      {/* Totals */}
      <div className="p-3">
        <div className="mb-1.5 flex items-center gap-1.5">
          <Sigma className="h-3 w-3 text-ink-muted" />
          <span className="text-[11px] font-semibold text-ink">合計列（選填）</span>
          <button
            type="button"
            onClick={addTotal}
            className="ml-auto inline-flex items-center gap-1 rounded border border-rule bg-white px-2 py-0.5 text-[10px] text-ink-muted transition-colors hover:border-primary hover:text-primary"
          >
            <Plus className="h-2.5 w-2.5" /> 合計列
          </button>
        </div>
        {totals.length === 0 ? (
          <div className="text-[10px] text-ink-faint">
            可不寫合計。chef 看 formula CEL 寫對應 aggregate（如 sum(items.amount)）。
          </div>
        ) : (
          <div className="space-y-1.5">
            {totals.map((t, idx) => (
              <TotalRow
                key={t.id}
                total={t}
                onPatch={p => updateTotal(idx, p)}
                onRemove={() => removeTotal(idx)}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function ItemFieldRow({ field, onPatch, onRemove, dragHandle }: {
  field: FormField
  onPatch: (p: Partial<FormField>) => void
  onRemove: () => void
  dragHandle?: SortableHandleProps
}) {
  return (
    <div
      ref={dragHandle?.setNodeRef}
      style={mergeDragStyle(dragHandle)}
      className={cn(
        'flex flex-wrap items-center gap-1.5 rounded border border-rule bg-white px-2 py-1',
        dragHandle?.isDragging && 'opacity-60 shadow-md',
      )}
    >
      <DragGrip handle={dragHandle} />
      <input
        value={field.id}
        onChange={e => onPatch({ id: e.target.value })}
        placeholder="snake_id"
        className="w-28 rounded border border-rule bg-white px-1.5 py-0.5 font-mono text-[10px] text-ink-muted outline-none focus:border-primary"
      />
      <input
        value={field.label['zh-TW']}
        onChange={e => onPatch({ label: { ...field.label, 'zh-TW': e.target.value } })}
        placeholder="顯示名稱"
        className="flex-1 min-w-[100px] rounded border border-rule bg-white px-1.5 py-0.5 text-xs text-ink outline-none focus:border-primary"
      />
      <select
        value={field.type}
        onChange={e => onPatch({ type: e.target.value as FieldType })}
        className="rounded border border-rule bg-white px-1 py-0.5 text-[10px] text-ink-muted outline-none focus:border-primary"
      >
        {FIELD_TYPE_OPTIONS.map(t => <option key={t} value={t}>{t}</option>)}
      </select>
      <label className="inline-flex items-center gap-1 text-[10px] text-ink-muted">
        <input
          type="checkbox"
          checked={field.required}
          onChange={e => onPatch({ required: e.target.checked })}
          className="h-3 w-3"
        />
        必填
      </label>
      <button onClick={onRemove} title="刪除欄位" className="text-ink-faint hover:text-danger">
        <Trash2 className="h-3 w-3" />
      </button>
    </div>
  )
}

function CountInput({ label, value, onChange }: {
  label: string
  value: number | undefined
  onChange: (n: number | undefined) => void
}) {
  return (
    <label className="inline-flex items-center gap-1 text-[10px] font-mono uppercase tracking-[0.12em] text-ink-muted">
      {label}
      <input
        type="number"
        min={0}
        value={value ?? ''}
        onChange={e => {
          const v = e.target.value
          onChange(v === '' ? undefined : Math.max(0, Number(v)))
        }}
        placeholder="—"
        className="w-12 rounded border border-rule bg-white px-1 py-0.5 text-center text-[11px] text-ink outline-none focus:border-primary"
      />
    </label>
  )
}

function TotalRow({ total, onPatch, onRemove }: {
  total: FormTotal
  onPatch: (p: Partial<FormTotal>) => void
  onRemove: () => void
}) {
  return (
    <div className="flex flex-wrap items-center gap-1.5 rounded border border-rule bg-white px-2 py-1">
      <Sigma className="h-3 w-3 text-ink-faint" />
      <input
        value={total.label['zh-TW']}
        onChange={e => onPatch({ label: { ...total.label, 'zh-TW': e.target.value } })}
        placeholder="顯示名稱"
        className="w-28 rounded border border-rule bg-white px-1.5 py-0.5 text-xs text-ink outline-none focus:border-primary"
      />
      <input
        value={total.formula}
        onChange={e => onPatch({ formula: e.target.value })}
        placeholder="CEL，譬如 sum(items.amount)"
        className="flex-1 min-w-[180px] rounded border border-rule bg-white px-1.5 py-0.5 font-mono text-[10px] text-ink-muted outline-none focus:border-primary"
      />
      <select
        value={total.format ?? 'number'}
        onChange={e => onPatch({ format: e.target.value as FormTotal['format'] })}
        className="rounded border border-rule bg-white px-1 py-0.5 text-[10px] text-ink-muted outline-none focus:border-primary"
      >
        {TOTAL_FORMATS.map(f => <option key={f} value={f}>{f}</option>)}
      </select>
      <button onClick={onRemove} title="刪除合計列" className="text-ink-faint hover:text-danger">
        <Trash2 className="h-3 w-3" />
      </button>
    </div>
  )
}

function collectItemFieldIds(layout: LayoutChild[]): string[] {
  const out: string[] = []
  for (const n of layout) {
    if (n.kind === 'fieldRef') out.push(n.id)
    else if (n.kind === 'row') for (const c of n.children) out.push(c.id)
    // banner / section / repeater not expected at item level — skip
  }
  return out
}

function stripFieldRefs(layout: LayoutChild[], removedFieldId: string): LayoutChild[] {
  return layout
    .map(n => {
      if (n.kind === 'fieldRef') return n.id === removedFieldId ? null : n
      if (n.kind === 'row') {
        return { ...n, children: n.children.filter(c => c.id !== removedFieldId) }
      }
      return n
    })
    .filter((x): x is LayoutChild => x !== null)
}

// ─────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────

function isSection(c: LayoutChild): c is FormSection {
  return c.kind === 'section'
}

const SEVERITY_TONE: Record<BannerSeverity, { border: string; bg: string; icon: React.ReactNode }> = {
  info:   { border: 'border-primary/30', bg: 'bg-primary/5',  icon: <Info className="h-3 w-3 text-primary" /> },
  warn:   { border: 'border-warn/30',    bg: 'bg-warn/5',     icon: <ShieldAlert className="h-3 w-3 text-warn" /> },
  danger: { border: 'border-danger/30',  bg: 'bg-danger/5',   icon: <ShieldAlert className="h-3 w-3 text-danger" /> },
}
