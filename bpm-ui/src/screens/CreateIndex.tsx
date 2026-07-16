import { Link } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { formRegistry } from '@/features/registry'
import { FORMS, type FormCode } from '@/lib/workflow'
import { resolveLauncherIcon } from '@/lib/launcherIcons'
import { useFlowRegistry, entryForVersion } from '@/hooks/useFlowRegistry'
import { routes } from '@/router'

interface CreateAction {
  code: FormCode
  label: string
  iconKey: string | null
}

interface CreateSection {
  key: string
  label: string
  groupIcon: string | null
  groupSort: number
  items: CreateAction[]
}

const OTHER_KEY = '__other__'

export function CreateIndex() {
  // Catalog-driven: the customer Create page mirrors the admin AI Kitchen
  // launcher-preview panel. Group / icon / order / approved-only all come
  // from admin's Flow data over /api/flow-registry; the compile-time
  // formRegistry stays the gate for *which* forms actually have a React
  // component to render (a flow can be Approved in admin before its chef
  // form ships). While the registry is still loading we fall back to
  // showing every registered manifest so the page never flashes empty.
  const { entries: registry } = useFlowRegistry()
  const gateByState = registry !== null

  // Gate each manifest on ITS OWN version's published state, not the latest
  // registry version: a newer Draft version (e.g. a freshly cloned V2) must
  // not hide the still-published V1 that the launcher actually renders
  // (/apply/:code opens the highest manifest version).
  const actions: Array<CreateAction & { groupKey: string; groupLabel: string; groupIcon: string | null; groupSort: number; order: number }> =
    [...formRegistry.values()]
      .filter(m => {
        if (!gateByState) return true
        return entryForVersion(registry, m.code, m.version)?.state === 'Published'
      })
      .map(m => {
        const entry = entryForVersion(registry, m.code, m.version)
        return {
          code: m.code,
          // Admin's Flow display name (from /api/flow-registry) is the source
          // of truth — same as icon/group/order below. Fall back to the
          // compile-time FORMS label only while the registry is still loading
          // or for a flow with no admin name set.
          label: entry?.displayName || FORMS[m.code]?.zhLabel || m.code,
          iconKey: entry?.iconKey ?? null,
          groupKey: entry?.groupCode ?? OTHER_KEY,
          groupLabel: entry?.groupDisplayName?.['zh-TW'] ?? entry?.groupCode ?? '其他',
          groupIcon: entry?.groupIcon ?? null,
          groupSort: entry?.groupSortOrder ?? Number.MAX_SAFE_INTEGER,
          order: entry?.displayOrder ?? 0,
        }
      })

  const sections = buildSections(actions)
  const total = sections.reduce((n, s) => n + s.items.length, 0)

  return (
    <div className="mx-auto max-w-screen-lg space-y-6 md:p-6">
      <div>
        <h1 className="text-2xl font-bold text-ink">Create a new case</h1>
        <p className="mt-1 text-sm text-ink-muted">Pick a form to start a new process instance.</p>
      </div>

      <SectionCard>
        <SectionTitle>Available flows</SectionTitle>
        {total === 0 ? (
          <div className="px-4 py-10 text-center text-sm text-ink-faint">
            目前沒有可用的流程 — 請聯絡管理員建置新流程。
          </div>
        ) : (
          <div className="space-y-5 p-4">
            {sections.map(s => {
              const GroupGlyph = resolveLauncherIcon(s.groupIcon)
              return (
                <div key={s.key}>
                  <div className="mb-2 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-[0.14em] text-ink-faint">
                    <GroupGlyph className="h-3.5 w-3.5" />
                    {s.label}
                    <span className="ml-1 font-mono text-[10px] text-ink-faint">{s.items.length}</span>
                  </div>
                  <div className="grid grid-cols-2 gap-2 md:grid-cols-3">
                    {s.items.map(a => {
                      const Glyph = resolveLauncherIcon(a.iconKey)
                      return (
                        <Link
                          key={a.code}
                          to={routes.formCreate(a.code)}
                          className="flex flex-col items-start gap-1 rounded-md border border-rule bg-white px-4 py-3 text-left transition-colors hover:border-primary/40 hover:bg-blue-50"
                        >
                          <span className="font-mono text-[10.5px] uppercase tracking-wider text-ink-faint">{a.code}</span>
                          <span className="flex items-center gap-1.5 text-sm font-medium text-ink">
                            <Glyph className="h-3.5 w-3.5 text-primary" />
                            {a.label}
                          </span>
                        </Link>
                      )
                    })}
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </SectionCard>
    </div>
  )
}

/** Group by groupKey, sort sections by groupSort, items by displayOrder
 *  then code; the unassigned '__other__' bucket sinks to the bottom. */
function buildSections(
  actions: Array<CreateAction & { groupKey: string; groupLabel: string; groupIcon: string | null; groupSort: number; order: number }>,
): CreateSection[] {
  const map = new Map<string, CreateSection & { _orders: Map<string, number> }>()
  for (const a of actions) {
    let section = map.get(a.groupKey)
    if (!section) {
      section = {
        key: a.groupKey,
        label: a.groupLabel,
        groupIcon: a.groupIcon,
        groupSort: a.groupKey === OTHER_KEY ? Number.MAX_SAFE_INTEGER : a.groupSort,
        items: [],
        _orders: new Map(),
      }
      map.set(a.groupKey, section)
    }
    section.items.push({ code: a.code, label: a.label, iconKey: a.iconKey })
    section._orders.set(a.code, a.order)
  }
  for (const s of map.values()) {
    s.items.sort((x, y) =>
      (s._orders.get(x.code)! - s._orders.get(y.code)!) || x.code.localeCompare(y.code))
  }
  return [...map.values()].sort((x, y) => x.groupSort - y.groupSort || x.label.localeCompare(y.label))
}
