import { useEffect, useRef, useState } from 'react'
import { Send, Bot, User, AlertTriangle } from 'lucide-react'
import { cn } from '@/lib/cn'
import type { OnboardingStep, DraftSpec } from '@/lib/onboarding'

/**
 * Co-Pilot Canvas — split layout (chat left, AI-generated canvas right).
 *
 * Sprint 3 #3: chat now calls bpm-svc /api/chat which proxies to Anthropic.
 * The backend swallows ANTHROPIC_API_KEY (so it never leaves the server) and
 * returns 503 + a structured payload when the key is unset, which we surface
 * inline so the developer knows exactly what to configure.
 */

const CHAT_API = (import.meta.env.VITE_BPM_SVC_URL ?? 'http://localhost:5290') + '/api/chat'

interface ChatMessage {
  role: 'assistant' | 'user'
  text: string
}

interface AnthropicTextBlock { type: 'text'; text: string }
interface AnthropicResponse { content?: AnthropicTextBlock[] }

const STEP_OPENERS: Record<string, string> = {
  source:    '請描述您要設計的流程，或選一個範本（LEAVE / PURCHASE）開始。需要建議哪個範本適合您嗎？',
  structure: '看一下右邊的 BPMN 骨架，您可以直接拖拉節點 / 連線。如有想加 / 改 / 刪的步驟，告訴我，我會建議怎麼動。',
  forms:     '右邊列出每個 user task 的欄位。要加新欄位、改型別、加條件規則都跟我說。',
  decisions: '每個 gateway 我都列在右邊。請告訴我每個 gateway 的條件——譬如「金額 > 50K 走 A 路徑」。',
  approvers: '請告訴我每個 approval 步驟由誰簽。我可以建議常見的審核者規則組合。',
  notify:    '預設我先給您雙語的 email 模板，您可以在右邊微調文字、變數、收件人。',
  sla:       '常見配置：審核 24 工時、超時 escalation 通知。要套用這個，還是您有不同需求？',
  test:      '我會用您的 spec 模擬幾張案件，請看右邊的測試結果。如果路徑不如預期，告訴我哪裡需要調整。',
  go_live:   '所有 validator 都過了。確認 spec 摘要正確嗎？按下「Submit Spec」就送到後台。',
}

function summarizeDraft(d: DraftSpec) {
  return {
    meta: d.meta,
    nodeCount: d.flow.nodes.length,
    edgeCount: d.flow.edges.length,
    nodes: d.flow.nodes.map(n => ({ id: n.id, type: n.type, label: n.label })),
    userTaskFormCodes: d.userTasks.map(t => ({ id: t.id, formCode: t.formCode, fieldCount: t.fields.length })),
    approvalCount: d.approvals.length,
    decisionCount: d.decisions.length,
    notificationCount: d.notifications.length,
    testCaseCount: d.testCases.length,
  }
}

export function CoPilotCanvas({
  step,
  draft,
  setDraft,
  canvas,
}: {
  step: OnboardingStep
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
  canvas: React.ReactNode
}) {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [input, setInput] = useState('')
  const [busy, setBusy] = useState(false)
  const [setupHint, setSetupHint] = useState<string | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)

  // Reset chat when step changes; seed with the step opener.
  useEffect(() => {
    setMessages([{ role: 'assistant', text: STEP_OPENERS[step.id] ?? '請開始這一步。' }])
    setSetupHint(null)
  }, [step.id])

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  // setDraft reserved for future tool-use (AI mutates draft directly).
  void setDraft

  const send = async () => {
    const text = input.trim()
    if (!text || busy) return
    const next: ChatMessage[] = [...messages, { role: 'user', text }]
    setMessages(next)
    setInput('')
    setBusy(true)
    setSetupHint(null)

    try {
      // Anthropic expects messages without our role labels — map assistant/user.
      const anthropicMessages = next
        .filter(m => m.text.trim().length > 0)
        .map(m => ({ role: m.role, content: m.text }))

      const res = await fetch(CHAT_API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          step: step.id,
          draftSummary: summarizeDraft(draft),
          messages: anthropicMessages,
        }),
      })

      if (res.status === 503) {
        const body = await res.json().catch(() => ({}))
        const msg = body?.message ?? 'Chat unavailable — backend not configured.'
        setSetupHint(msg)
        setBusy(false)
        return
      }
      if (!res.ok) {
        const body = await res.text()
        throw new Error(`HTTP ${res.status} — ${body || res.statusText}`)
      }

      const data = await res.json() as AnthropicResponse
      const replyText = (data.content ?? [])
        .filter((b): b is AnthropicTextBlock => b.type === 'text')
        .map(b => b.text)
        .join('\n\n')
        .trim() || '(AI returned no text content.)'

      setMessages(m => [...m, { role: 'assistant', text: replyText }])
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setMessages(m => [...m, { role: 'assistant', text: `⚠️ 呼叫 AI 失敗：${msg}` }])
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid h-[640px] grid-cols-[380px_1fr] gap-3">
      {/* Left — Chat */}
      <div className="flex flex-col rounded-md border border-rule bg-card">
        <div className="border-b border-rule bg-slate-50 px-3 py-2">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">Chat</p>
          <p className="text-xs text-ink-faint">{step.brief}</p>
        </div>

        <div ref={scrollRef} className="flex-1 overflow-y-auto p-3 space-y-3">
          {messages.map((m, i) => (
            <div key={i} className={cn('flex items-start gap-2', m.role === 'user' && 'flex-row-reverse')}>
              <div className={cn(
                'flex h-7 w-7 shrink-0 items-center justify-center rounded-full',
                m.role === 'assistant' ? 'bg-blue-100 text-blue-700' : 'bg-slate-100 text-slate-700',
              )}>
                {m.role === 'assistant' ? <Bot className="h-4 w-4" /> : <User className="h-4 w-4" />}
              </div>
              <div className={cn(
                'max-w-[280px] whitespace-pre-wrap rounded-lg px-3 py-2 text-sm leading-snug',
                m.role === 'assistant' ? 'bg-slate-50 text-ink' : 'bg-primary text-white',
              )}>
                {m.text}
              </div>
            </div>
          ))}
          {busy && (
            <div className="flex items-start gap-2">
              <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-blue-100 text-blue-700">
                <Bot className="h-4 w-4" />
              </div>
              <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-ink-muted italic">
                AI 思考中…
              </div>
            </div>
          )}
          {setupHint && (
            <div className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <div className="break-words font-mono">{setupHint}</div>
            </div>
          )}
        </div>

        <div className="border-t border-rule bg-slate-50 p-2">
          <div className="flex items-center gap-1.5">
            <input
              value={input}
              onChange={e => setInput(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send() } }}
              placeholder={busy ? '送出中…' : '跟 AI 說明這個 step…'}
              disabled={busy}
              className="h-8 flex-1 rounded-md border border-rule bg-white px-3 text-sm focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary disabled:bg-slate-100"
            />
            <button
              onClick={send}
              disabled={busy}
              className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-white hover:bg-blue-700 disabled:bg-slate-300"
            >
              <Send className="h-4 w-4" />
            </button>
          </div>
          <p className="mt-1 text-[10px] text-ink-faint">
            POST /api/chat → Anthropic Claude Sonnet 4.6（system prompt 走 prompt cache）
          </p>
        </div>
      </div>

      {/* Right — Canvas */}
      <div className="flex flex-col rounded-md border border-rule bg-card overflow-hidden">
        <div className="border-b border-rule bg-slate-50 px-3 py-2 flex items-center justify-between">
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">Canvas — {step.en}</p>
            <p className="text-xs text-ink-faint">由 AI 即時生成的問卷 / 預覽，您也可以直接在這裡編輯</p>
          </div>
          <span className="rounded bg-blue-50 px-2 py-0.5 text-[10px] font-mono uppercase tracking-wider text-blue-700">
            spec.{step.id}
          </span>
        </div>
        <div className="flex-1 overflow-y-auto p-4">
          {canvas}
        </div>
      </div>
    </div>
  )
}
