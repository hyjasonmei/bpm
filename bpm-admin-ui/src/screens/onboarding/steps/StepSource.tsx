import { useRef, useState } from 'react'
import { Upload, FileText, Sparkles, AlertTriangle, Loader2 } from 'lucide-react'
import { Field, Input } from '@/components/ui/form'
import {
  type DraftSpec,
  type FlowNode,
  type FlowEdge,
  LEAVE_PRESET,
  PURCHASE_PRESET,
  EMPTY_DRAFT,
} from '@/lib/onboarding'
import { parseBpmnXml } from '@/lib/bpmnXmlParse'
import { api, ApiError } from '@/flowcook/api'
import { BpmnEditor } from '@/components/BpmnEditor'

const TEMPLATES = [
  { code: 'LEAVE',    name: '請假',     preset: LEAVE_PRESET },
  { code: 'PURCHASE', name: '採購申請', preset: PURCHASE_PRESET },
]

interface ExtractedSkeleton {
  meta: { tenant: string; flowName: string; flowCode: string }
  nodes: FlowNode[]
  edges: FlowEdge[]
  confidence_notes?: string
}

export function StepSource({ draft, setDraft }: { draft: DraftSpec; setDraft: (d: DraftSpec) => void }) {
  const [scratchText, setScratchText] = useState('')
  const [busyKind, setBusyKind] = useState<null | 'image' | 'description'>(null)
  const [error, setError] = useState<string | null>(null)
  const [confNotes, setConfNotes] = useState<string | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  const updateMeta = (patch: Partial<DraftSpec['meta']>) =>
    setDraft({ ...draft, meta: { ...draft.meta, ...patch } })

  const loadPreset = (preset: Partial<DraftSpec>) => {
    setDraft({ ...EMPTY_DRAFT, ...preset, meta: { ...EMPTY_DRAFT.meta, ...preset.meta } })
  }

  const applySkeleton = (s: ExtractedSkeleton) => {
    setDraft({
      ...EMPTY_DRAFT,
      meta: { ...EMPTY_DRAFT.meta, ...s.meta },
      flow: { nodes: s.nodes, edges: s.edges },
    })
    setConfNotes(s.confidence_notes ?? null)
  }

  const callExtract = async (payload: object) => {
    setError(null)
    setConfNotes(null)
    try {
      const skeleton = await api<ExtractedSkeleton>('/api/spec-extract', {
        method: 'POST',
        json: payload,
      })
      applySkeleton(skeleton)
    } catch (e: unknown) {
      if (e instanceof ApiError && e.status === 503) {
        let msg = 'AI 不可用 — 後端未配置 ANTHROPIC_API_KEY'
        try { msg = JSON.parse(e.body)?.message ?? msg } catch { /* keep default */ }
        setError(msg)
        return
      }
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  const handleUpload = async (file: File) => {
    const isBpmnXml = /\.(bpmn|xml)$/i.test(file.name)
                   || file.type === 'application/xml'
                   || file.type === 'text/xml'
    if (isBpmnXml) {
      setBusyKind('image') // reuse spinner state — visual feedback for any upload
      setError(null)
      setConfNotes(null)
      try {
        const text = await file.text()
        const parsed = parseBpmnXml(text)
        setDraft({
          ...EMPTY_DRAFT,
          meta: {
            ...EMPTY_DRAFT.meta,
            ...draft.meta,
            flowName: draft.meta.flowName || parsed.flowName,
          },
          flow: { nodes: parsed.nodes, edges: parsed.edges },
        })
        if (parsed.warnings.length > 0) {
          setConfNotes(`BPMN 解析提示：\n${parsed.warnings.map(w => `· ${w}`).join('\n')}`)
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
      } finally {
        setBusyKind(null)
      }
      return
    }
    if (!file.type.startsWith('image/')) {
      setError(`只支援 PNG / JPG 圖片或 .bpmn / .xml 檔案。收到的是 ${file.type || file.name}。`)
      return
    }
    setBusyKind('image')
    try {
      const dataUrl = await fileToDataUrl(file)
      await callExtract({ kind: 'image', dataUrl })
    } finally {
      setBusyKind(null)
    }
  }

  const handleFromScratch = async () => {
    if (!scratchText.trim()) return
    setBusyKind('description')
    try {
      await callExtract({ kind: 'description', text: scratchText.trim() })
    } finally {
      setBusyKind(null)
    }
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
        <p className="mb-3 text-xs text-ink-muted">擇一：上傳既有流程圖、選範本、或從零開始描述。</p>
        <div className="grid grid-cols-3 gap-3">
          {/* Upload */}
          <SourceCard
            icon={busyKind === 'image' ? <Loader2 className="h-5 w-5 animate-spin" /> : <Upload className="h-5 w-5" />}
            title="Upload 檔案"
            subtitle="PNG / JPG（AI 看圖）或 .bpmn / .xml（直接 import）"
            active={busyKind === 'image'}
            disabled={busyKind !== null}
          >
            <input
              ref={fileRef}
              type="file"
              accept="image/png,image/jpeg,image/webp,application/xml,text/xml,.bpmn,.xml"
              hidden
              onChange={e => {
                const f = e.target.files?.[0]
                if (f) handleUpload(f)
                e.target.value = ''
              }}
            />
            <button
              onClick={() => fileRef.current?.click()}
              disabled={busyKind !== null}
              className="mt-2 w-full rounded border border-rule bg-white px-2.5 py-1.5 text-xs hover:bg-slate-50 disabled:opacity-50"
            >
              {busyKind === 'image' ? 'Claude vision 分析中…' : '選擇檔案 →'}
            </button>
            <p className="mt-1 text-[10px] italic text-ink-faint">
              .bpmn / .xml 走純解析（免 AI、免 token）
            </p>
          </SourceCard>

          {/* Templates */}
          <SourceCard icon={<Sparkles className="h-5 w-5" />} title="Templates" subtitle="從業界範本開始" active>
            <div className="mt-2 flex flex-col gap-1.5">
              {TEMPLATES.map(t => (
                <button
                  key={t.code}
                  onClick={() => loadPreset(t.preset)}
                  disabled={busyKind !== null}
                  className="flex items-center justify-between rounded border border-rule bg-white px-2.5 py-1.5 text-xs hover:bg-slate-50 disabled:opacity-50"
                >
                  <span><span className="font-mono text-ink-faint">{t.code}</span> {t.name}</span>
                  <span className="text-blue-600">Load →</span>
                </button>
              ))}
            </div>
          </SourceCard>

          {/* From Scratch */}
          <SourceCard
            icon={busyKind === 'description' ? <Loader2 className="h-5 w-5 animate-spin" /> : <FileText className="h-5 w-5" />}
            title="From Scratch"
            subtitle="自然語言描述，AI 抽 BPMN"
            active={busyKind === 'description'}
            disabled={busyKind !== null}
          >
            <textarea
              value={scratchText}
              onChange={e => setScratchText(e.target.value)}
              disabled={busyKind !== null}
              placeholder="例：員工填差旅申請 → 主管批 → 金額 ≥ 5 萬要 CEO 核准 → 財務出帳"
              className="mt-2 h-20 w-full resize-none rounded border border-rule bg-white px-2 py-1 text-xs focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary disabled:bg-slate-50"
            />
            <button
              onClick={handleFromScratch}
              disabled={busyKind !== null || !scratchText.trim()}
              className="mt-1.5 w-full rounded bg-primary px-2.5 py-1.5 text-xs font-semibold text-white hover:bg-blue-700 disabled:bg-slate-300"
            >
              {busyKind === 'description' ? 'AI 思考中…' : '抽出 BPMN →'}
            </button>
          </SourceCard>
        </div>
      </section>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-rose-300 bg-rose-50 px-3 py-2 text-xs text-rose-800">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <div className="break-words font-mono">{error}</div>
        </div>
      )}

      {confNotes && (
        <div className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900">
          <p className="mb-1 font-semibold">AI 信心度註記</p>
          <p className="whitespace-pre-wrap">{confNotes}</p>
        </div>
      )}

      {draft.flow.nodes.length > 0 && (
        <section>
          <div className="mb-2 flex items-baseline justify-between">
            <h3 className="text-sm font-semibold text-ink">BPMN 預覽（可直接拖拉編輯）</h3>
            <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
              {draft.flow.nodes.length} nodes · {draft.flow.edges.length} edges
            </span>
          </div>
          <p className="mb-2 text-xs text-ink-muted">
            左邊聊完 AI 會把流程畫到這。您可以直接拖拉節點、拉線、按 Delete 移除；改動會即時 sync 回 spec。
          </p>
          <BpmnEditor draft={draft} onChange={setDraft} height={420} />
        </section>
      )}
    </div>
  )
}

function fileToDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(reader.error ?? new Error('FileReader failed'))
    reader.readAsDataURL(file)
  })
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
    <div className={`rounded-md border p-3 ${active ? 'border-primary bg-blue-50/40' : 'border-rule bg-white'} ${disabled ? 'opacity-70' : ''}`}>
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
