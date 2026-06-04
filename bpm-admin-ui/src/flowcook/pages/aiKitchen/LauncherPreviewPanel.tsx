import { useEffect, useMemo, useState } from 'react'
import { cn } from '@/lib/cn'
import { SortableList, DragGrip, mergeDragStyle } from '@/components/sortable-list/SortableList'
import {
  ICON_CATALOG,
  ICON_NAMES,
  resolveIcon,
} from '@/flowcook/pages/sitesetting/FlowGroupsTab'
import { reorderFlows, setFlowIcon, type FlowSummary } from '@/flowcook/api/flows'
import type { FlowGroupDto } from '@/flowcook/api/flowGroups'

/**
 * Right-rail "launcher preview" for the AI Kitchen page. Mirrors what the
 * bpm employee Create page renders for these flows — grouped by launcher
 * group (group sortOrder), ordered within each group by the flow's
 * DisplayOrder — and lets the admin curate that view in place:
 *
 *  • drag a flow to reorder it within its group  → POST /api/flows/reorder
 *  • click a flow's icon to pick a launcher icon → POST /api/flows/{id}/icon
 *  • "approved only" mirrors the live page's gating (the customer Create
 *    page only offers Approved flows).
 *
 * The customer page reads the same group/icon/order/state over
 * /api/flow-registry at runtime, so this preview is the real thing, not a
 * mock. Icon names are constrained to the shared ICON_CATALOG so the
 * picker can't drift into names bpm-ui can't resolve.
 */

const UNGROUPED = '__ungrouped__'

interface GroupSection {
  key: string
  label: string
  icon: string | null
  flows: FlowSummary[]
}

export function LauncherPreviewPanel({
  flows,
  groups,
  onChanged,
}: {
  flows: FlowSummary[]
  groups: FlowGroupDto[]
  /** Refetch flows after a mutation so the panel + the main list stay in sync. */
  onChanged: () => Promise<void>
}) {
  const [approvedOnly, setApprovedOnly] = useState(true)
  // Local mirror so a drag reorders instantly; re-synced whenever the
  // parent refetch lands new props.
  const [local, setLocal] = useState<FlowSummary[]>(flows)
  const [busy, setBusy] = useState(false)
  const [iconPickerFor, setIconPickerFor] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => { setLocal(flows) }, [flows])

  const sections = useMemo<GroupSection[]>(() => {
    const visible = approvedOnly ? local.filter(f => f.state === 'Approved') : local
    const groupsBySort = [...groups].sort((a, b) => a.sortOrder - b.sortOrder)

    const byGroup = new Map<string, FlowSummary[]>()
    for (const f of visible) {
      const known = f.groupId && groups.some(g => g.id === f.groupId)
      const key = known ? f.groupId! : UNGROUPED
      const arr = byGroup.get(key) ?? []
      arr.push(f)
      byGroup.set(key, arr)
    }

    const sortFlows = (arr: FlowSummary[]) =>
      [...arr].sort((a, b) => a.displayOrder - b.displayOrder || a.flowCode.localeCompare(b.flowCode))

    const out: GroupSection[] = []
    for (const g of groupsBySort) {
      const arr = byGroup.get(g.id)
      if (arr && arr.length) {
        out.push({ key: g.id, label: g.displayName['zh-TW'] ?? g.code, icon: g.icon, flows: sortFlows(arr) })
      }
    }
    const ungrouped = byGroup.get(UNGROUPED)
    if (ungrouped && ungrouped.length) {
      out.push({ key: UNGROUPED, label: '其他', icon: null, flows: sortFlows(ungrouped) })
    }
    return out
  }, [local, groups, approvedOnly])

  async function persist(fn: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await fn()
      await onChanged()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Action failed')
      await onChanged() // resync to truth on failure
    } finally {
      setBusy(false)
    }
  }

  function handleReorder(next: FlowSummary[]) {
    // Optimistic: splice the new order back into the flat local list so the
    // drag sticks while the POST round-trips.
    const nextIds = next.map(f => f.id)
    setLocal(prev => {
      const others = prev.filter(f => !nextIds.includes(f.id))
      return [...others, ...next]
    })
    void persist(() => reorderFlows(nextIds))
  }

  function handlePickIcon(flowId: string, iconKey: string | null) {
    setIconPickerFor(null)
    void persist(() => setFlowIcon(flowId, iconKey))
  }

  const totalShown = sections.reduce((n, s) => n + s.flows.length, 0)

  return (
    <div className="flex min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
      <header className="flex items-center justify-between border-b border-rule px-4 py-3">
        <div>
          <h3 className="text-sm font-semibold text-ink">Launcher preview</h3>
          <p className="mt-0.5 text-[11px] text-ink-muted">員工在 Create 頁看到的樣子 — 拖曳排序、點圖示換 icon</p>
        </div>
        <label className="flex cursor-pointer items-center gap-1.5 text-[11px] text-ink-muted">
          <input
            type="checkbox"
            checked={approvedOnly}
            onChange={e => setApprovedOnly(e.target.checked)}
            className="h-3.5 w-3.5 accent-primary"
          />
          Approved only
        </label>
      </header>

      {error && (
        <div className="border-b border-danger/30 bg-danger/5 px-4 py-2 text-[11px] text-danger">{error}</div>
      )}

      <div className={cn('flex-1 overflow-auto p-3', busy && 'pointer-events-none opacity-60')}>
        {totalShown === 0 ? (
          <div className="px-3 py-10 text-center text-xs text-ink-muted">
            {approvedOnly
              ? '尚無 Approved 流程 — 核准後才會出現在員工的 Create 頁。'
              : '尚無流程。'}
          </div>
        ) : (
          <div className="space-y-4">
            {sections.map(section => {
              const SectionIcon = resolveIcon(section.icon)
              return (
                <div key={section.key}>
                  <div className="mb-1.5 flex items-center gap-1.5 px-1 font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
                    <SectionIcon className="h-3 w-3" />
                    {section.label}
                    <span className="text-ink-faint">· {section.flows.length}</span>
                  </div>
                  <SortableList
                    items={section.flows}
                    getId={f => f.id}
                    onReorder={handleReorder}
                  >
                    {(f, _idx, handle) => {
                      const Icon = resolveIcon(f.iconKey)
                      const picking = iconPickerFor === f.id
                      return (
                        <div
                          ref={handle.setNodeRef}
                          style={mergeDragStyle(handle)}
                          className={cn(
                            'relative mb-1.5 flex items-center gap-2 rounded border border-rule bg-bg/40 px-2 py-1.5',
                            handle.isDragging && 'shadow-md ring-1 ring-primary/40',
                          )}
                        >
                          <DragGrip handle={handle} />
                          <button
                            type="button"
                            onClick={() => setIconPickerFor(picking ? null : f.id)}
                            title="選擇 launcher 圖示"
                            className={cn(
                              'flex h-7 w-7 shrink-0 items-center justify-center rounded border transition-colors',
                              picking
                                ? 'border-primary bg-primary/10 text-primary'
                                : 'border-rule bg-card text-ink-muted hover:border-primary hover:text-primary',
                            )}
                          >
                            <Icon className="h-3.5 w-3.5" />
                          </button>
                          <div className="min-w-0 flex-1">
                            <div className="truncate text-xs font-medium text-ink">{f.displayName}</div>
                            <div className="truncate font-mono text-[10px] text-ink-faint">{f.flowCode} · v{f.version}</div>
                          </div>

                          {picking && (
                            <IconPickerPopover
                              current={f.iconKey}
                              onPick={name => handlePickIcon(f.id, name)}
                              onClose={() => setIconPickerFor(null)}
                            />
                          )}
                        </div>
                      )
                    }}
                  </SortableList>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}

function IconPickerPopover({
  current,
  onPick,
  onClose,
}: {
  current: string | null
  onPick: (name: string | null) => void
  onClose: () => void
}) {
  return (
    <>
      {/* Click-away backdrop. */}
      <div className="fixed inset-0 z-10" onClick={onClose} />
      <div className="absolute right-2 top-9 z-20 w-48 rounded-lg border border-rule bg-card p-2 shadow-xl">
        <div className="mb-1.5 flex items-center justify-between px-1">
          <span className="font-mono text-[9px] tracking-[0.14em] uppercase text-ink-muted">Icon</span>
          <button
            type="button"
            onClick={() => onPick(null)}
            className="text-[10px] text-ink-faint hover:text-ink"
          >
            清除
          </button>
        </div>
        <div className="grid grid-cols-6 gap-1">
          {ICON_NAMES.map(name => {
            const I = ICON_CATALOG[name]
            const active = current === name
            return (
              <button
                key={name}
                type="button"
                onClick={() => onPick(name)}
                title={name}
                className={cn(
                  'flex h-6 w-6 items-center justify-center rounded border transition-colors',
                  active
                    ? 'border-primary bg-primary/10 text-primary'
                    : 'border-transparent text-ink-muted hover:border-rule hover:text-primary',
                )}
              >
                <I className="h-3.5 w-3.5" />
              </button>
            )
          })}
        </div>
      </div>
    </>
  )
}
