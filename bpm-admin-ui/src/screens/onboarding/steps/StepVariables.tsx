import { EyeOff, Plus, Trash2 } from 'lucide-react'
import type { DraftSpec, FlowVariable } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

/**
 * VARIABLES step.
 *
 * Flow-scoped variables referenced as `${var_name}` in later steps'
 * expression fields. Sensitive values get a UI mask (eye-off icon) so
 * casual screen shares don't leak them; persistence is plain text in
 * admin-svc for v0.
 */
export function StepVariables({ draft, setDraft }: Props) {
  function setVars(next: FlowVariable[]) {
    setDraft({ ...draft, variables: next })
  }

  function update(i: number, patch: Partial<FlowVariable>) {
    setVars(draft.variables.map((v, idx) => idx === i ? { ...v, ...patch } : v))
  }
  function remove(i: number) {
    setVars(draft.variables.filter((_, idx) => idx !== i))
  }
  function add() {
    setVars([...draft.variables, { name: '', defaultValue: '', description: '', sensitive: false }])
  }

  const hasIntegrations = (draft.integrations.items ?? []).length > 0

  return (
    <div className="space-y-4">
      <div>
        <h3 className="text-sm font-semibold text-ink">流程變數 · Variables</h3>
        <p className="mt-1 text-xs text-ink-muted">
          可在後續表達式 / 條件 / 訊息模板用 <code className="font-mono text-[11px]">{'${'}var_name{'}'}</code> 引用。
          純內部流程多半不需要 — 直接 Next。INTEGRATIONS 的 BASE_URL 之類會自動帶到這。
        </p>
      </div>

      <div className="rounded-md border border-rule bg-card">
        {draft.variables.length === 0 && (
          <div className="px-4 py-10 text-center">
            <p className="text-sm font-medium text-ink">這關通常可空</p>
            <p className="mt-1 text-xs text-ink-muted">
              {hasIntegrations
                ? '上一關有設整合，可以從 baseUrl 抽變數出來（未來會自動推；目前先手動加）。'
                : '純內部流程不需要變數，直接下方 Next。'}
            </p>
            <p className="mt-3 text-xs text-ink-faint">想自訂變數（譬如自動核准上限）才需要點下方「加變數」。</p>
          </div>
        )}
        {draft.variables.length > 0 && (
          <table className="w-full text-sm">
            <thead className="border-b border-rule bg-label-bg text-left font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              <tr>
                <th className="px-3 py-2 font-normal">Name</th>
                <th className="px-3 py-2 font-normal">Default value</th>
                <th className="px-3 py-2 font-normal">Description</th>
                <th className="px-3 py-2 font-normal">Sensitive</th>
                <th className="px-3 py-2 font-normal"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-rule">
              {draft.variables.map((v, i) => (
                <tr key={i}>
                  <td className="px-3 py-2">
                    <input
                      value={v.name}
                      onChange={(e) => update(i, { name: e.target.value })}
                      placeholder="MAX_AUTO_APPROVE"
                      className={inputCls + ' font-mono'}
                    />
                  </td>
                  <td className="px-3 py-2">
                    <div className="relative">
                      <input
                        type={v.sensitive ? 'password' : 'text'}
                        value={v.defaultValue}
                        onChange={(e) => update(i, { defaultValue: e.target.value })}
                        placeholder="50000"
                        className={inputCls + (v.sensitive ? ' pr-7' : '')}
                      />
                      {v.sensitive && (
                        <EyeOff className="pointer-events-none absolute right-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-ink-faint" />
                      )}
                    </div>
                  </td>
                  <td className="px-3 py-2">
                    <input
                      value={v.description ?? ''}
                      onChange={(e) => update(i, { description: e.target.value })}
                      placeholder="optional"
                      className={inputCls}
                    />
                  </td>
                  <td className="px-3 py-2 text-center">
                    <input
                      type="checkbox"
                      checked={v.sensitive}
                      onChange={(e) => update(i, { sensitive: e.target.checked })}
                    />
                  </td>
                  <td className="px-3 py-2 text-right">
                    <button
                      onClick={() => remove(i)}
                      className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-danger/10 hover:text-danger"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="border-t border-rule px-3 py-2">
          <button
            onClick={add}
            className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary"
          >
            <Plus className="h-3 w-3" /> 加變數
          </button>
        </div>
      </div>
    </div>
  )
}

const inputCls = "block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
