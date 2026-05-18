import { useMemo } from 'react'
import { Languages, Sparkles } from 'lucide-react'
import type { DraftSpec } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

/**
 * Step 10 — TRANSLATION.
 *
 * Aggregates user-visible labels from the spec into a single table; the
 * authoring locale (zh-TW) is pre-filled, the wizard's job is to make
 * sure other locales (en in MVP) are populated.
 *
 * "AI fill empties" is stubbed — calling Anthropic to translate empties
 * is a follow-up; the button explains itself in MVP.
 */
export function StepTranslation({ draft, setDraft }: Props) {
  const labels = draft.labels ?? { 'zh-TW': {}, en: {} }
  const keys = useMemo(() => collectLabelKeys(draft), [draft])

  function updateLabel(locale: string, key: string, text: string) {
    const next = { ...labels, [locale]: { ...(labels[locale] ?? {}), [key]: text } }
    setDraft({ ...draft, labels: next })
  }

  function aiFillStub() {
    window.alert('AI fill empties 將在後續整合 Anthropic 翻譯 — 目前請手動填入。')
  }

  if (keys.length === 0) {
    return (
      <div className="rounded-md border border-dashed border-rule bg-bg/50 px-4 py-8 text-center text-sm text-ink-muted">
        尚無可翻譯的 label — 先在 SOURCE / FORMS 填好名稱再回這一步。
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold text-ink flex items-center gap-2">
            <Languages className="h-4 w-4 text-primary" />
            多語系翻譯 · Translation
          </h3>
          <p className="mt-1 text-xs text-ink-muted">
            收集 spec 內所有使用者可見的 label，補各語系翻譯。建立此 bundle 的人預設為 zh-TW。
          </p>
        </div>
        <button
          onClick={aiFillStub}
          className="inline-flex items-center gap-1.5 rounded border border-dashed border-accent/40 bg-card px-2.5 py-1 text-xs font-medium text-accent hover:bg-accent/10"
        >
          <Sparkles className="h-3.5 w-3.5" />
          AI fill empties（即將上線）
        </button>
      </div>

      <div className="overflow-auto rounded-md border border-rule bg-card">
        <table className="w-full text-sm">
          <thead className="sticky top-0 border-b border-rule bg-label-bg text-left font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            <tr>
              <th className="px-3 py-2 font-normal">Key</th>
              <th className="px-3 py-2 font-normal">zh-TW</th>
              <th className="px-3 py-2 font-normal">en</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-rule">
            {keys.map((k) => (
              <tr key={k.key}>
                <td className="px-3 py-2 align-top">
                  <div className="font-mono text-[11px] text-ink-muted">{k.key}</div>
                  <div className="text-[11px] text-ink-faint">{k.context}</div>
                </td>
                <td className="px-3 py-2">
                  <input
                    value={labels['zh-TW']?.[k.key] ?? k.defaultZh}
                    onChange={(e) => updateLabel('zh-TW', k.key, e.target.value)}
                    className={inputCls}
                  />
                </td>
                <td className="px-3 py-2">
                  <input
                    value={labels.en?.[k.key] ?? ''}
                    onChange={(e) => updateLabel('en', k.key, e.target.value)}
                    placeholder="(empty)"
                    className={inputCls}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

interface LabelKey {
  key: string
  context: string
  defaultZh: string
}

function collectLabelKeys(draft: DraftSpec): LabelKey[] {
  const out: LabelKey[] = []
  if (draft.meta.flowName) {
    out.push({ key: 'flow.name', context: 'flow display name', defaultZh: draft.meta.flowName })
  }
  for (const node of draft.flow.nodes) {
    if (node.label) {
      out.push({ key: `node.${node.id}.label`, context: `node ${node.id} (${node.type})`, defaultZh: node.label })
    }
  }
  for (const ut of draft.userTasks) {
    for (const f of ut.fields) {
      const zh = f.label['zh-TW']
      if (zh) {
        out.push({
          key: `field.${ut.id}.${f.id}.label`,
          context: `${ut.formCode} → ${f.id}`,
          defaultZh: zh,
        })
      }
    }
  }
  return out
}

const inputCls = "block w-full rounded border border-rule bg-white px-2 py-1 text-xs text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
