/**
 * StepIntegrations (v2) — bound to BPMN serviceTask nodes.
 *
 * One IntegrationItem ↔ one serviceTask node. Empty state when the flow
 * has no serviceTask node (pure internal flows are routinely empty —
 * the wizard says so explicitly). Pill row + active editor when there
 * are multiple, matching FORMS / DECISIONS / APPROVERS / NOTIFY.
 *
 * Side effect: when an integration save introduces or updates a baseUrl
 * for a serviceTask, draft.variables auto-gains a `${name}_BASE_URL`
 * entry so VARIABLES (step 8) surfaces it. The variable is read-only
 * (UI-side) when sourced from an integration.
 *
 * Preset cards (Slack webhook / Generic REST / Internal OA) seed the
 * common combos so the customer doesn't start from an empty form.
 */
import { useState } from 'react'
import { AlertCircle, CheckCircle2, Plug, Plus, Sparkles, Trash2 } from 'lucide-react'
import { cn } from '@/lib/cn'
import type { DraftSpec, IntegrationItem, FlowVariable } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

interface PresetIntegration {
  label: string
  desc: string
  build: (serviceTaskNodeId: string, nodeLabel: string) => Omit<IntegrationItem, 'id'>
}

const PRESETS: PresetIntegration[] = [
  {
    label: 'Slack webhook',
    desc: 'POST 一個 channel webhook',
    build: (nodeId, label) => ({
      name: `${label} (Slack)`,
      baseUrl: 'https://hooks.slack.com',
      endpoint: 'POST /services/<WORKSPACE>/<CHANNEL>/<TOKEN>',
      serviceTaskNodeId: nodeId,
      auth: { kind: 'none' },
    }),
  },
  {
    label: 'Generic REST',
    desc: '一般 API 呼叫 + bearer token',
    build: (nodeId, label) => ({
      name: label,
      baseUrl: 'https://api.example.com',
      endpoint: 'POST /v1/resource',
      serviceTaskNodeId: nodeId,
      auth: { kind: 'bearer' },
    }),
  },
  {
    label: 'Internal OA / ERP',
    desc: '走 header auth 寫進 OA 系統',
    build: (nodeId, label) => ({
      name: `${label} (OA)`,
      baseUrl: 'https://oa.acme.example',
      endpoint: 'POST /api/orders',
      serviceTaskNodeId: nodeId,
      auth: { kind: 'header' },
    }),
  },
]

export function StepIntegrations({ draft, setDraft }: Props) {
  const serviceTaskNodes = draft.flow.nodes.filter(n => n.type === 'serviceTask')
  const items = draft.integrations.items ?? []
  const [activeNodeId, setActiveNodeId] = useState<string>(serviceTaskNodes[0]?.id ?? '')

  if (serviceTaskNodes.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-rule bg-bg/50 px-4 py-10 text-center">
        <p className="text-sm font-medium text-ink">這關通常可空</p>
        <p className="mt-1 text-xs text-ink-muted">
          請假 / 加班 / 簽核這類純內部流程不需要 serviceTask 節點，直接下方 Next。
        </p>
        <p className="mt-3 text-xs text-ink-faint">
          要對接 OA / ERP / Slack / Webhook → 先到 SOURCE 拉一個 serviceTask 節點，這裡就會出現對應的整合設定。
        </p>
      </div>
    )
  }

  const safeActiveId = serviceTaskNodes.find(n => n.id === activeNodeId)?.id ?? serviceTaskNodes[0].id
  const activeNode = serviceTaskNodes.find(n => n.id === safeActiveId)!
  const activeItem = items.find(it => it.serviceTaskNodeId === safeActiveId)

  function persist(nextItems: IntegrationItem[]) {
    // Side effect: derive `<NAME>_BASE_URL` variables from each
    // integration that has a baseUrl. Existing ones with the same name
    // get refreshed; user-added variables are left untouched.
    const derived: FlowVariable[] = nextItems
      .filter(it => it.baseUrl?.trim())
      .map(it => ({
        name: deriveVarName(it),
        defaultValue: it.baseUrl,
        description: `Base URL (自 INTEGRATIONS：${it.name || it.id})`,
        sensitive: false,
      }))
    const derivedNames = new Set(derived.map(v => v.name))
    const keptUserVars = draft.variables.filter(v => !derivedNames.has(v.name))
    setDraft({
      ...draft,
      integrations: { ...draft.integrations, items: nextItems },
      variables: [...derived, ...keptUserVars],
    })
  }

  function upsert(item: IntegrationItem) {
    const others = items.filter(it => it.id !== item.id)
    persist([...others, item])
  }

  function removeItem(id: string) {
    persist(items.filter(it => it.id !== id))
  }

  function addBlank() {
    const id = `integ_${Date.now().toString(36).slice(-4)}`
    upsert({
      id,
      name: activeNode.label,
      baseUrl: '',
      serviceTaskNodeId: safeActiveId,
      auth: { kind: 'none' },
    })
  }

  function addPreset(p: PresetIntegration) {
    const id = `integ_${Date.now().toString(36).slice(-4)}`
    upsert({ id, ...p.build(safeActiveId, activeNode.label) })
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-xs text-ink-muted">
        每個 BPMN <code className="rounded bg-slate-100 px-1 py-0.5 text-[11px]">serviceTask</code>{' '}
        節點都應該綁一個 integration（外部 API 呼叫）。設了 baseUrl 後會自動產生{' '}
        <code className="font-mono text-[11px]">{`\${NAME_BASE_URL}`}</code>{' '}
        變數，在 step 8 VARIABLES 看得到。
      </p>

      {/* Pill row — one per serviceTask node */}
      {serviceTaskNodes.length > 1 && (
        <div className="flex flex-wrap gap-1.5">
          {serviceTaskNodes.map(node => {
            const it = items.find(x => x.serviceTaskNodeId === node.id)
            const ok = !!it?.baseUrl?.trim()
            const isActive = node.id === safeActiveId
            return (
              <button
                key={node.id}
                onClick={() => setActiveNodeId(node.id)}
                className={cn(
                  'flex items-center gap-2 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors',
                  isActive
                    ? 'border-primary bg-primary/5 text-ink'
                    : 'border-rule bg-card text-ink-muted hover:border-primary/40 hover:text-ink',
                )}
              >
                {ok
                  ? <CheckCircle2 className={cn('h-3.5 w-3.5', isActive ? 'text-good' : 'text-good/70')} />
                  : <AlertCircle className={cn('h-3.5 w-3.5', isActive ? 'text-warn' : 'text-warn/70')} />}
                <Plug className={cn('h-3 w-3', isActive ? 'text-primary' : 'text-ink-faint')} />
                <span>{node.label}</span>
                {it?.baseUrl && (
                  <span className={cn('truncate font-mono text-[10px] max-w-[160px]', isActive ? 'text-ink-muted' : 'text-ink-faint')}>
                    {it.baseUrl.replace(/^https?:\/\//, '')}
                  </span>
                )}
              </button>
            )
          })}
        </div>
      )}

      {/* Active serviceTask integration editor */}
      <div className="rounded-md border border-rule bg-white">
        <div className="flex items-center justify-between gap-3 border-b border-rule bg-slate-50 px-3 py-2">
          <div className="flex items-center gap-2">
            <Plug className="h-4 w-4 text-primary" />
            <span className="text-sm font-semibold text-ink">{activeNode.label}</span>
            <span className="font-mono text-[10px] text-ink-faint">{activeNode.id}</span>
          </div>
          {activeItem && (
            <button
              onClick={() => removeItem(activeItem.id)}
              className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-rose-50 hover:text-danger"
              title="移除這個整合（保留 BPMN 節點）"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          )}
        </div>

        {!activeItem ? (
          <div className="space-y-3 p-4">
            <p className="text-xs text-ink-muted">
              此 serviceTask 還沒綁定 integration。
            </p>
            <PresetStrip onPick={addPreset} onBlank={addBlank} />
          </div>
        ) : (
          <div className="space-y-3 p-3">
            <div className="grid grid-cols-2 gap-3">
              <Field label="Name">
                <input
                  value={activeItem.name}
                  onChange={e => upsert({ ...activeItem, name: e.target.value })}
                  placeholder="e.g. CRM Sync"
                  className={inputCls}
                />
              </Field>
              <Field label="ID" hint="snake_case，會用在衍生變數名稱">
                <input
                  value={activeItem.id}
                  onChange={e => upsert({ ...activeItem, id: e.target.value.toLowerCase().replace(/\s+/g, '_') })}
                  className={inputCls + ' font-mono'}
                />
              </Field>
              <Field label="Base URL" hint="存 baseUrl 會自動產生衍生變數">
                <input
                  value={activeItem.baseUrl}
                  onChange={e => upsert({ ...activeItem, baseUrl: e.target.value })}
                  placeholder="https://api.example.com"
                  className={inputCls}
                />
              </Field>
              <Field label="OpenAPI URL (optional)" hint="未來會 parse endpoint 列表">
                <input
                  value={activeItem.openApiUrl ?? ''}
                  onChange={e => upsert({ ...activeItem, openApiUrl: e.target.value })}
                  placeholder="https://.../openapi.json"
                  className={inputCls}
                />
              </Field>
              <Field label="Endpoint" hint="HTTP method + path，後續 OpenAPI parser 上線後改下拉">
                <input
                  value={activeItem.endpoint ?? ''}
                  onChange={e => upsert({ ...activeItem, endpoint: e.target.value })}
                  placeholder="POST /v1/leads"
                  className={inputCls}
                />
              </Field>
              <Field label="Auth">
                <div className="flex items-center gap-2">
                  <select
                    value={activeItem.auth?.kind ?? 'none'}
                    onChange={e => upsert({ ...activeItem, auth: { kind: e.target.value as 'none' | 'bearer' | 'header', secret: activeItem.auth?.secret } })}
                    className={inputCls + ' max-w-[120px]'}
                  >
                    <option value="none">none</option>
                    <option value="bearer">bearer</option>
                    <option value="header">header</option>
                  </select>
                  {activeItem.auth?.kind && activeItem.auth.kind !== 'none' && (
                    <input
                      type="password"
                      value={activeItem.auth?.secret ?? ''}
                      onChange={e => upsert({ ...activeItem, auth: { kind: activeItem.auth!.kind, secret: e.target.value } })}
                      placeholder="secret"
                      className={inputCls}
                    />
                  )}
                </div>
              </Field>
            </div>

            {activeItem.auth?.kind !== 'none' && activeItem.auth?.secret && (
              <p className="rounded border border-warn/30 bg-warn/5 px-2 py-1 text-[11px] text-ink">
                ⚠ v0 secret 以明文存於 spec — 上線前會搬到 Site Setting 的 secret store，spec 只留 ref。
              </p>
            )}

            {activeItem.baseUrl && (
              <p className="rounded border border-good/30 bg-good/5 px-2 py-1 text-[11px] text-ink">
                ✓ 已自動產生變數{' '}
                <code className="font-mono">{`\${${deriveVarName(activeItem)}}`}</code>{' '}
                — 到 step 8 VARIABLES 可看 / 改名
              </p>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function PresetStrip({
  onPick, onBlank,
}: { onPick: (p: PresetIntegration) => void; onBlank: () => void }) {
  return (
    <div className="rounded-md border border-dashed border-rule bg-slate-50/40 p-2">
      <div className="mb-1.5 flex items-center gap-1.5">
        <Sparkles className="h-3.5 w-3.5 text-primary" />
        <p className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">範本 — 一鍵新增</p>
      </div>
      <div className="flex flex-wrap gap-1">
        {PRESETS.map((p, i) => (
          <button
            key={i}
            type="button"
            onClick={() => onPick(p)}
            title={p.desc}
            className="flex flex-col items-start rounded border border-rule bg-white px-2 py-1 text-left hover:border-primary"
          >
            <span className="text-[11px] font-medium text-ink">{p.label}</span>
            <span className="text-[10px] text-ink-faint">{p.desc}</span>
          </button>
        ))}
        <button
          type="button"
          onClick={onBlank}
          className="flex items-center gap-1 rounded border border-dashed border-rule bg-white px-2 py-1 text-[11px] font-medium text-ink-muted hover:border-primary hover:text-primary"
        >
          <Plus className="h-3 w-3" />
          空白整合
        </button>
      </div>
    </div>
  )
}

function deriveVarName(it: IntegrationItem): string {
  const base = (it.id || it.name || 'integration').toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_|_$/g, '')
  return `${base}_BASE_URL`
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</span>
      {children}
      {hint && <p className="mt-1 text-[10px] text-ink-faint">{hint}</p>}
    </label>
  )
}

const inputCls = "block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
