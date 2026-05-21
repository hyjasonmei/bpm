import { useEffect } from 'react'
import { AlertTriangle, ChevronDown, GripVertical, Info, Plus, ShieldAlert, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import {
  buildDefaultLayout,
  collectLayoutFieldIds,
  type BannerSeverity,
  type FieldColSpan,
  type FieldRef,
  type FormBanner,
  type FormField,
  type FormRow,
  type FormSection,
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
      {sections.map((section) => {
        const layoutIdx = layout.indexOf(section)
        return (
          <SectionCard
            key={section.id}
            section={section}
            fields={task.fields}
            unplaced={unplaced}
            onSectionPatch={p => updateSection(layoutIdx, p)}
            onRemoveSection={() => removeSection(layoutIdx)}
            onChildrenChange={children => setSectionChildren(layoutIdx, children)}
          />
        )
      })}

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
  section, fields, unplaced, onSectionPatch, onRemoveSection, onChildrenChange,
}: {
  section: FormSection
  fields: FormField[]
  unplaced: FormField[]
  onSectionPatch: (p: Partial<FormSection>) => void
  onRemoveSection: () => void
  onChildrenChange: (children: LayoutChild[]) => void
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
  function updateChild(idx: number, patch: Partial<LayoutChild>) {
    const next = children.map((c, i) => i === idx ? ({ ...c, ...patch } as LayoutChild) : c)
    onChildrenChange(next)
  }
  function removeChild(idx: number) {
    onChildrenChange(children.filter((_, i) => i !== idx))
  }

  return (
    <div className="rounded-md border border-rule bg-card">
      {/* Section header */}
      <div className="flex items-center gap-2 border-b border-rule bg-slate-50/60 px-3 py-2">
        <GripVertical className="h-3.5 w-3.5 shrink-0 text-ink-faint" />
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
        {children.map((child, idx) => {
          if (child.kind === 'fieldRef') {
            const fld = fields.find(f => f.id === child.id)
            return (
              <FieldRefRow
                key={`${child.id}-${idx}`}
                fieldRef={child}
                field={fld}
                onPatch={p => updateChild(idx, p as Partial<FieldRef>)}
                onRemove={() => removeChild(idx)}
              />
            )
          }
          if (child.kind === 'row') {
            return (
              <RowCard
                key={child.id}
                row={child}
                fields={fields}
                unplaced={unplaced}
                onPatch={p => updateChild(idx, p as Partial<FormRow>)}
                onRemove={() => removeChild(idx)}
              />
            )
          }
          if (child.kind === 'banner') {
            return (
              <BannerCard
                key={child.id}
                banner={child}
                onPatch={p => updateChild(idx, p as Partial<FormBanner>)}
                onRemove={() => removeChild(idx)}
              />
            )
          }
          // Defensive: nested section inside a section. Schema permits
          // it but the UI doesn't expose nesting controls; render a
          // disabled card so existing data isn't lost.
          if (child.kind === 'section') {
            return (
              <div key={child.id} className="rounded border border-dashed border-rule p-2 text-[11px] text-ink-muted">
                嵌套區塊「{child.title['zh-TW']}」— 此 UI 不支援編輯，請改成 row / banner。
              </div>
            )
          }
          return null
        })}
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
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────
// Sub-cards
// ─────────────────────────────────────────────────────────────────────

function FieldRefRow({ fieldRef, field, onPatch, onRemove }: {
  fieldRef: FieldRef
  field: FormField | undefined
  onPatch: (p: Partial<FieldRef>) => void
  onRemove: () => void
}) {
  const missing = !field
  return (
    <div className={cn(
      'flex items-center gap-2 rounded border px-2.5 py-1.5',
      missing ? 'border-danger/30 bg-danger/5' : 'border-rule bg-white',
    )}>
      <GripVertical className="h-3 w-3 shrink-0 text-ink-faint" />
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

function RowCard({ row, fields, unplaced, onPatch, onRemove }: {
  row: FormRow
  fields: FormField[]
  unplaced: FormField[]
  onPatch: (p: Partial<FormRow>) => void
  onRemove: () => void
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
    <div className="rounded border border-rule bg-slate-50/40">
      <div className="flex items-center gap-2 border-b border-rule px-2.5 py-1.5">
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
        {row.children.map((cell, idx) => {
          const fld = fields.find(f => f.id === cell.id)
          return (
            <div
              key={`${cell.id}-${idx}`}
              className={cn(
                'flex items-center gap-2 rounded border bg-white px-2.5 py-1.5',
                fld ? 'border-rule' : 'border-danger/30 bg-danger/5',
              )}
            >
              <GripVertical className="h-3 w-3 shrink-0 text-ink-faint" />
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
        })}
        <UnplacedFieldPicker options={unplaced} onPick={addCell} compact />
      </div>
    </div>
  )
}

function BannerCard({ banner, onPatch, onRemove }: {
  banner: FormBanner
  onPatch: (p: Partial<FormBanner>) => void
  onRemove: () => void
}) {
  const tone = SEVERITY_TONE[banner.severity]
  return (
    <div className={cn('rounded border px-2.5 py-2', tone.border, tone.bg)}>
      <div className="mb-1 flex items-center gap-2">
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
