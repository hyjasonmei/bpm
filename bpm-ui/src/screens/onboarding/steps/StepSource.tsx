import { Upload, FileText, Sparkles } from 'lucide-react'
import { Field, Input } from '@/components/ui/form'
import { type DraftSpec, LEAVE_PRESET, PURCHASE_PRESET, EMPTY_DRAFT } from '@/lib/onboarding'

const TEMPLATES = [
  { code: 'LEAVE',    name: '請假',     preset: LEAVE_PRESET },
  { code: 'PURCHASE', name: '採購申請', preset: PURCHASE_PRESET },
]

export function StepSource({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const updateMeta = (patch: Partial<DraftSpec['meta']>) =>
    setDraft({ ...draft, meta: { ...draft.meta, ...patch } })

  const loadPreset = (preset: Partial<DraftSpec>) => {
    setDraft({ ...EMPTY_DRAFT, ...preset, meta: { ...EMPTY_DRAFT.meta, ...preset.meta } })
  }

  return (
    <div className="flex flex-col gap-5">
      <section>
        <h3 className="mb-3 text-sm font-semibold text-ink">基本資訊</h3>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Tenant 代號" required hint="客戶識別碼，譬如 acme">
            <Input
              value={draft.meta.tenant}
              onChange={e => updateMeta({ tenant: e.target.value.toLowerCase().replace(/\s+/g, '-') })}
              placeholder="acme"
            />
          </Field>
          <Field label="Flow Code" required hint="用於 class / table 命名，UPPERCASE">
            <Input
              value={draft.meta.flowCode}
              onChange={e => updateMeta({ flowCode: e.target.value.toUpperCase().replace(/\s+/g, '_') })}
              placeholder="LEAVE"
              maxLength={20}
            />
          </Field>
          <Field label="Flow 名稱（中文）" required className="col-span-2">
            <Input
              value={draft.meta.flowName}
              onChange={e => updateMeta({ flowName: e.target.value })}
              placeholder="請假"
            />
          </Field>
        </div>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold text-ink">流程來源</h3>
        <p className="mb-3 text-xs text-ink-muted">擇一：上傳既有流程圖、選範本、或從零開始。</p>
        <div className="grid grid-cols-3 gap-3">
          <SourceCard icon={<Upload className="h-5 w-5" />} title="Upload" subtitle="PPT / Visio / 手繪 / Excel"
            disabled hint="Phase B 後啟用（VLM 抽 BPMN）" />
          <SourceCard icon={<Sparkles className="h-5 w-5" />} title="Templates" subtitle="從業界範本開始"
            active>
            <div className="mt-2 flex flex-col gap-1.5">
              {TEMPLATES.map(t => (
                <button key={t.code} onClick={() => loadPreset(t.preset)}
                  className="flex items-center justify-between rounded border border-rule bg-white px-2.5 py-1.5 text-xs hover:bg-slate-50">
                  <span><span className="font-mono text-ink-faint">{t.code}</span> {t.name}</span>
                  <span className="text-blue-600">Load →</span>
                </button>
              ))}
            </div>
          </SourceCard>
          <SourceCard icon={<FileText className="h-5 w-5" />} title="From Scratch" subtitle="只描述，AI 慢慢問"
            disabled hint="Phase B 後啟用（需即時 Claude API）" />
        </div>
      </section>

      {draft.flow.nodes.length > 0 && (
        <section>
          <h3 className="mb-2 text-sm font-semibold text-ink">已載入：{draft.flow.nodes.length} 個節點</h3>
          <div className="rounded border border-rule bg-slate-50 p-3 font-mono text-[11px] text-ink-muted">
            {draft.flow.nodes.map(n => (
              <div key={n.id}>
                <span className="text-ink-faint">{n.type.padEnd(13)}</span>
                <span className="text-ink">{n.label}</span>
                <span className="ml-2 text-ink-faint">[{n.id}]</span>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  )
}

function SourceCard({
  icon, title, subtitle, active, disabled, hint, children,
}: {
  icon: React.ReactNode
  title: string
  subtitle: string
  active?: boolean
  disabled?: boolean
  hint?: string
  children?: React.ReactNode
}) {
  return (
    <div className={`rounded-md border p-3 ${active ? 'border-primary bg-blue-50/40' : 'border-rule bg-white'} ${disabled ? 'opacity-50' : ''}`}>
      <div className="flex items-center gap-2">
        <div className={`flex h-9 w-9 items-center justify-center rounded ${active ? 'bg-primary text-white' : 'bg-slate-100 text-ink-muted'}`}>
          {icon}
        </div>
        <div>
          <p className="text-sm font-semibold text-ink">{title}</p>
          <p className="text-[11px] text-ink-faint">{subtitle}</p>
        </div>
      </div>
      {hint && <p className="mt-2 text-[10px] italic text-ink-faint">{hint}</p>}
      {children}
    </div>
  )
}
