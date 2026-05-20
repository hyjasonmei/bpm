import { Plus, Trash2 } from 'lucide-react'
import type { DraftSpec, IntegrationItem } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

/**
 * Step 8 — INTEGRATIONS.
 *
 * Each item declares a single outbound endpoint the flow may call. v0
 * captures name + baseUrl + optional OpenAPI URL + endpoint string +
 * trigger node + auth mode. OpenAPI parsing + field mapping editor
 * are deferred — the textarea form keeps the schema forward-compatible.
 */
export function StepIntegrations({ draft, setDraft }: Props) {
  const items = draft.integrations.items ?? []
  const flowNodeIds = draft.flow.nodes.map(n => n.id)

  function setItems(next: IntegrationItem[]) {
    setDraft({ ...draft, integrations: { ...draft.integrations, items: next } })
  }

  function update(i: number, patch: Partial<IntegrationItem>) {
    setItems(items.map((it, idx) => idx === i ? { ...it, ...patch } : it))
  }
  function remove(i: number) {
    setItems(items.filter((_, idx) => idx !== i))
  }
  function add() {
    setItems([
      ...items,
      {
        id: `integ-${items.length + 1}`,
        name: '',
        baseUrl: '',
        auth: { kind: 'none' },
      },
    ])
  }

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-ink">外部整合 · Integrations</h3>
        <p className="mt-1 text-xs text-ink-muted">
          流程節點要呼叫的外部 HTTP API（記錄 baseUrl / OpenAPI URL / endpoint）。
          純內部流程多半不需要 — 直接 Next。
        </p>
      </div>

      {items.length === 0 && (
        <div className="rounded-md border border-dashed border-rule bg-bg/50 px-4 py-10 text-center">
          <p className="text-sm font-medium text-ink">這關通常可空</p>
          <p className="mt-1 text-xs text-ink-muted">
            請假 / 加班 / 簽核這類純內部流程不對接外部系統，直接下方 Next。
          </p>
          <p className="mt-3 text-xs text-ink-faint">要對接 OA / ERP / Slack / Webhook 才需要點下方「加整合」。</p>
        </div>
      )}

      {items.map((it, i) => (
        <div key={i} className="space-y-2 rounded-md border border-rule bg-card p-4">
          <div className="flex items-center justify-between">
            <span className="font-mono text-[11px] text-ink-muted">{it.id}</span>
            <button
              onClick={() => remove(i)}
              className="inline-flex items-center gap-1 rounded text-xs text-ink-faint hover:text-danger"
              title="移除"
            >
              <Trash2 className="h-3.5 w-3.5" />
            </button>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Name">
              <input
                value={it.name}
                onChange={(e) => update(i, { name: e.target.value })}
                placeholder="e.g. CRM Sync"
                className={inputCls}
              />
            </Field>
            <Field label="Trigger node">
              <select
                value={it.triggerNodeId ?? ''}
                onChange={(e) => update(i, { triggerNodeId: e.target.value || undefined })}
                className={inputCls}
              >
                <option value="">— select node —</option>
                {flowNodeIds.map(id => <option key={id} value={id}>{id}</option>)}
              </select>
            </Field>
            <Field label="Base URL">
              <input
                value={it.baseUrl}
                onChange={(e) => update(i, { baseUrl: e.target.value })}
                placeholder="https://api.example.com"
                className={inputCls}
              />
            </Field>
            <Field label="OpenAPI URL (optional)">
              <input
                value={it.openApiUrl ?? ''}
                onChange={(e) => update(i, { openApiUrl: e.target.value })}
                placeholder="https://.../openapi.json"
                className={inputCls}
              />
            </Field>
            <Field label="Endpoint">
              <input
                value={it.endpoint ?? ''}
                onChange={(e) => update(i, { endpoint: e.target.value })}
                placeholder="POST /v1/leads"
                className={inputCls}
              />
            </Field>
            <Field label="Auth">
              <div className="flex items-center gap-2">
                <select
                  value={it.auth?.kind ?? 'none'}
                  onChange={(e) => update(i, { auth: { kind: e.target.value as 'none' | 'bearer' | 'header', secret: it.auth?.secret } })}
                  className={inputCls + ' max-w-[120px]'}
                >
                  <option value="none">none</option>
                  <option value="bearer">bearer</option>
                  <option value="header">header</option>
                </select>
                {it.auth?.kind && it.auth.kind !== 'none' && (
                  <input
                    type="password"
                    value={it.auth?.secret ?? ''}
                    onChange={(e) => update(i, { auth: { kind: it.auth!.kind, secret: e.target.value } })}
                    placeholder="secret"
                    className={inputCls}
                  />
                )}
              </div>
            </Field>
          </div>
        </div>
      ))}

      <button
        onClick={add}
        className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary"
      >
        <Plus className="h-3 w-3" /> 加整合
      </button>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</span>
      {children}
    </label>
  )
}

const inputCls = "block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
