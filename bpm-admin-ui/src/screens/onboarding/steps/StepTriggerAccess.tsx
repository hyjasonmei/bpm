import { Plus, Trash2 } from 'lucide-react'
import type { DraftSpec, FlowTrigger } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

/**
 * Step 2 — TRIGGER & ACCESS.
 *
 * v0/v1 accepts exactly one form trigger; the schema is still
 * `triggers[]` so future versions can add cron / webhook / mail.
 *
 * Principal references are kept as free-form strings in MVP. A future
 * iteration will swap them for the User & Role principal picker —
 * placeholder is intentional so the schema is forward-compatible.
 */
export function StepTriggerAccess({ draft, setDraft }: Props) {
  const trigger = draft.triggers[0]
  const userTaskCodes = Array.from(new Set(draft.userTasks.map(t => t.formCode).filter(Boolean)))

  function updateTrigger(next: FlowTrigger | null) {
    setDraft({ ...draft, triggers: next ? [next] : [] })
  }

  function setLaunchableBy(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, launchableBy: list } })
  }
  function setVisibleTo(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, visibleTo: list } })
  }
  function setWatcher(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, watcher: list } })
  }

  return (
    <div className="space-y-6">
      <SectionHeading>觸發 · Trigger</SectionHeading>
      <p className="text-xs text-ink-muted">
        指定一個觸發本流程的表單。v0 只支援 form trigger；未來會加 cron / webhook / mail。
      </p>

      <div className="rounded-md border border-rule bg-card p-4">
        <Field label="Trigger 名稱（ID）">
          <input
            value={trigger?.id ?? ''}
            onChange={(e) => updateTrigger({
              id: e.target.value,
              type: 'form',
              formCode: trigger?.formCode ?? '',
            })}
            placeholder="e.g. leave-form"
            className={inputCls}
          />
        </Field>
        <Field label="Form Code（綁定 user task）">
          {userTaskCodes.length === 0 ? (
            <input
              value={trigger?.formCode ?? ''}
              onChange={(e) => updateTrigger({
                id: trigger?.id ?? 'main',
                type: 'form',
                formCode: e.target.value.toUpperCase(),
              })}
              placeholder="LEAVE"
              className={inputCls}
            />
          ) : (
            <select
              value={trigger?.formCode ?? ''}
              onChange={(e) => updateTrigger({
                id: trigger?.id ?? 'main',
                type: 'form',
                formCode: e.target.value,
              })}
              className={inputCls}
            >
              <option value="">— select —</option>
              {userTaskCodes.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          )}
        </Field>
        {trigger && (
          <button
            onClick={() => updateTrigger(null)}
            className="mt-2 inline-flex items-center gap-1 rounded border border-danger/30 bg-card px-2.5 py-1 text-xs font-medium text-danger transition-colors hover:bg-danger/10"
          >
            <Trash2 className="h-3 w-3" /> 移除 trigger
          </button>
        )}
      </div>

      <SectionHeading>存取權限 · Access</SectionHeading>
      <PrincipalList
        label="launchable_by — 誰可以啟動"
        items={draft.access.launchableBy}
        onChange={setLaunchableBy}
        placeholder="user:alice 或 group:hr"
      />
      <PrincipalList
        label="visible_to — 誰可以在目錄看到"
        items={draft.access.visibleTo}
        onChange={setVisibleTo}
        placeholder="dept:finance"
      />
      <PrincipalList
        label="watcher — 旁觀者（可看別人的 instance）"
        items={draft.access.watcher}
        onChange={setWatcher}
        placeholder="role:auditor"
      />
    </div>
  )
}

function PrincipalList({
  label, items, onChange, placeholder,
}: { label: string; items: string[]; onChange: (l: string[]) => void; placeholder: string }) {
  return (
    <div className="rounded-md border border-rule bg-card p-4">
      <Field label={label}>
        <div className="space-y-1.5">
          {items.map((item, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                value={item}
                onChange={(e) => onChange(items.map((x, idx) => idx === i ? e.target.value : x))}
                placeholder={placeholder}
                className={inputCls}
              />
              <button
                onClick={() => onChange(items.filter((_, idx) => idx !== i))}
                className="flex h-7 w-7 items-center justify-center rounded text-ink-faint hover:bg-danger/10 hover:text-danger"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
          <button
            onClick={() => onChange([...items, ''])}
            className="inline-flex items-center gap-1 rounded border border-dashed border-rule bg-card px-2.5 py-1 text-xs font-medium text-ink-muted hover:border-primary hover:text-primary"
          >
            <Plus className="h-3 w-3" /> 加一個 principal
          </button>
        </div>
      </Field>
    </div>
  )
}

function SectionHeading({ children }: { children: React.ReactNode }) {
  return <h3 className="text-sm font-semibold text-ink">{children}</h3>
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">{label}</span>
      {children}
    </label>
  )
}

const inputCls = "block w-full rounded border border-rule bg-white px-3 py-1.5 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
