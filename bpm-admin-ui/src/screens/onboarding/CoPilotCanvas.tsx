import { useEffect, useRef, useState } from 'react'
import { Send, Bot, User, AlertTriangle, Wand2 } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { cn } from '@/lib/cn'
import type { OnboardingStep, DraftSpec } from '@/lib/onboarding'
import { STEP_TOOLS } from '@/lib/onboardingTools'
import { api, ApiError } from '@/flowcook/api'

/**
 * Co-Pilot Canvas — split layout (chat left, AI-generated canvas right).
 *
 * Sprint 3 #3: chat now calls bpm-svc /api/chat which proxies to Anthropic.
 * The backend swallows ANTHROPIC_API_KEY (so it never leaves the server) and
 * returns 503 + a structured payload when the key is unset, which we surface
 * inline so the developer knows exactly what to configure.
 *
 * Tool use: per-step tools in onboardingTools.ts let Claude mutate the draft
 * directly. When the response contains a tool_use block, we apply it and tell
 * the user. Both backends produce the same shape — the API path uses native
 * Anthropic tools; the CLI path emulates them via a sentinel-delimited prompt
 * contract that the backend re-wraps as tool_use blocks.
 */

/**
 * Renders assistant chat text as Markdown. Chat bubble is narrow
 * (~320 px) so the element overrides keep margins tight — only
 * top-level paragraphs get a top margin after the first one. Lists
 * stay snug; code uses tabular numbers + a small monospace font;
 * fenced code blocks scroll horizontally rather than push the bubble
 * out. Links open in a new tab.
 */
function AssistantMarkdown({ text }: { text: string }) {
  return (
    <div className="space-y-1.5">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          p: ({ children }) => <p className="leading-snug">{children}</p>,
          ul: ({ children }) => <ul className="ml-4 list-disc space-y-0.5">{children}</ul>,
          ol: ({ children }) => <ol className="ml-4 list-decimal space-y-0.5">{children}</ol>,
          li: ({ children }) => <li className="leading-snug">{children}</li>,
          strong: ({ children }) => <strong className="font-semibold text-ink">{children}</strong>,
          em: ({ children }) => <em className="italic">{children}</em>,
          a: ({ href, children }) => (
            <a href={href} target="_blank" rel="noreferrer" className="text-primary underline underline-offset-2 hover:text-blue-700">
              {children}
            </a>
          ),
          code: ({ className, children, ...rest }) => {
            const isInline = !className
            if (isInline) {
              return <code className="rounded bg-slate-200/70 px-1 py-0.5 font-mono text-[12px] text-ink" {...rest}>{children}</code>
            }
            return <code className={cn(className, 'font-mono')} {...rest}>{children}</code>
          },
          pre: ({ children }) => (
            <pre className="overflow-x-auto rounded-md bg-slate-900 px-3 py-2 text-[12px] leading-snug text-slate-100">
              {children}
            </pre>
          ),
          h1: ({ children }) => <h1 className="text-base font-semibold text-ink">{children}</h1>,
          h2: ({ children }) => <h2 className="text-sm font-semibold text-ink">{children}</h2>,
          h3: ({ children }) => <h3 className="text-sm font-semibold text-ink">{children}</h3>,
          blockquote: ({ children }) => (
            <blockquote className="border-l-2 border-rule pl-2 italic text-ink-muted">{children}</blockquote>
          ),
          hr: () => <hr className="my-2 border-rule" />,
          table: ({ children }) => (
            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-[12px]">{children}</table>
            </div>
          ),
          th: ({ children }) => <th className="border border-rule px-1.5 py-0.5 text-left font-semibold">{children}</th>,
          td: ({ children }) => <td className="border border-rule px-1.5 py-0.5 align-top">{children}</td>,
        }}
      >
        {text}
      </ReactMarkdown>
    </div>
  )
}

interface ChatMessage {
  role: 'assistant' | 'user'
  text: string
  /** assistant only — set when this turn applied a tool_use to the draft */
  appliedTool?: string
}

interface AnthropicTextBlock { type: 'text'; text: string }
interface AnthropicToolUseBlock { type: 'tool_use'; id: string; name: string; input: unknown }
type AnthropicBlock = AnthropicTextBlock | AnthropicToolUseBlock
interface AnthropicResponse {
  content?: AnthropicBlock[]
}

const STEP_OPENERS: Record<string, string> = {
  source:         '描述您要設計的流程（譬如「員工填差旅單 → 主管簽 → 金額大於 5 萬要 CEO 批」），我會直接幫您畫成 BPMN 顯示在右邊。或是選 LEAVE / PURCHASE 範本起手。',
  trigger_access: '這關決定誰能啟動 / 旁觀這個流程（能啟動的自然看得到，所以沒分開）。送單表單會自動取流程第一個 user task。這邊我只負責答疑跟給建議 — 實際勾選請用右邊 picker，比較直覺也不會出錯。',
  variables:      '純內部流程通常這關直接 Next 即可。如果上一關 INTEGRATIONS 設了外部 API，這裡會自動帶入 BASE_URL 之類的值，您可以改名 / 鎖定 / 移除；要再加自訂變數（譬如「自動核准上限 = 50000」）也可以。',
  forms:          '右邊列出每個 user task 的欄位。要加新欄位、改型別、加條件規則都跟我說。',
  decisions:      '每個 gateway 我都列在右邊。請告訴我每個 gateway 的條件——譬如「金額 > 50K 走 A 路徑」。',
  approvers:      '請告訴我每個 approval 步驟由誰簽。我可以建議常見的審核者規則組合。',
  notify:         '預設我先給您雙語的 email 模板，您可以在右邊微調文字、變數、收件人。',
  integrations:   '純內部流程（請假 / 加班 / 簽核這類）通常這關直接 Next 即可。要對接外部系統再告訴我目標系統 / OpenAPI URL / 想觸發的節點，我會幫您列出 endpoint 跟欄位映射。',
  sla:            '常見配置：審核 24 工時、超時 escalation 通知。要套用這個，還是您有不同需求？',
  translation:    '右邊列出所有 spec 內的 label。需要中英文對照可以一起聊，我會把翻譯填回右邊表格。',
  notes:          '有要交代給 chef（產 code 的 AI）或驗收者的補充嗎？右邊 textarea 直接寫；chef 之後如有疑問也會 append 在這。',
}

function summarizeDraft(d: DraftSpec) {
  return {
    meta: d.meta,
    nodeCount: d.flow.nodes.length,
    edgeCount: d.flow.edges.length,
    nodes: d.flow.nodes.map(n => ({ id: n.id, type: n.type, label: n.label })),
    // Full edge list — tools that emit references to edges (e.g.
    // emit_decision_rules) MUST use these exact ids verbatim. Without
    // this the model invents semantic names like `edge_days_to_vp`
    // which apply() can't resolve.
    edges: d.flow.edges.map(e => ({
      id: e.id, source: e.source, target: e.target,
      label: e.label, condition: e.condition, isDefault: e.isDefault,
    })),
    // userTask field IDs — gateway conditions / validators / notify
    // templates all reference these by exact id (e.g. `leave_days`,
    // NOT `leaveDays`). Surface the canonical id + type so the model
    // doesn't camelCase-ify them.
    userTasks: d.userTasks.map(t => ({
      id: t.id,
      formCode: t.formCode,
      fields: t.fields.map(f => ({ id: f.id, type: f.type, required: f.required })),
    })),
    approvalCount: d.approvals.length,
    decisionCount: d.decisions.length,
    // Surface current notifications + SLA so emit_notifications /
    // emit_sla_config can preserve existing entries when the model is
    // asked to "add"/"modify". Without this the model commonly drops
    // unmentioned entries because the tools have「replace COMPLETE
    // list」semantics.
    notifications: d.notifications.map(n => ({
      id: n.id, trigger: n.trigger, nodeId: n.nodeId,
      recipients: n.recipients.length, channel: n.channel,
    })),
    sla: d.sla?.perNode
      ? Object.fromEntries(Object.entries(d.sla.perNode).map(([nodeId, cfg]) => [nodeId, {
          duration: (cfg as { duration?: string }).duration,
          businessHoursOnly: (cfg as { businessHoursOnly?: boolean }).businessHoursOnly,
          escalation: (cfg as { escalation?: { after?: string; action?: string } }).escalation,
        }]))
      : {},
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
  const [elapsed, setElapsed] = useState(0)
  const scrollRef = useRef<HTMLDivElement>(null)

  // Reset chat when step changes; seed with the step opener.
  useEffect(() => {
    setMessages([{ role: 'assistant', text: STEP_OPENERS[step.id] ?? '請開始這一步。' }])
    setSetupHint(null)
  }, [step.id])

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  // Tick once per second while waiting on the backend so the spinner shows
  // "AI 思考中… 12s" instead of an opaque spinner — CLI cold starts can take
  // 30s+ and a frozen UI looks broken.
  useEffect(() => {
    if (!busy) { setElapsed(0); return }
    const t = setInterval(() => setElapsed(e => e + 1), 1000)
    return () => clearInterval(t)
  }, [busy])

  const send = async () => {
    const text = input.trim()
    if (!text || busy) return
    const next: ChatMessage[] = [...messages, { role: 'user', text }]
    setMessages(next)
    setInput('')
    setBusy(true)
    setSetupHint(null)

    const stepTool = STEP_TOOLS[step.id]

    try {
      // Anthropic expects messages without our role labels — map assistant/user.
      const anthropicMessages = next
        .filter(m => m.text.trim().length > 0)
        .map(m => ({ role: m.role, content: m.text }))

      let data: AnthropicResponse
      try {
        data = await api<AnthropicResponse>('/api/chat', {
          method: 'POST',
          json: {
            step: step.id,
            draftSummary: summarizeDraft(draft),
            messages: anthropicMessages,
            ...(stepTool ? { tools: [stepTool.tool] } : {}),
          },
        })
      } catch (err) {
        if (err instanceof ApiError && err.status === 503) {
          let msg = 'Chat unavailable — backend not configured.'
          try { msg = JSON.parse(err.body)?.message ?? msg } catch { /* keep default */ }
          setSetupHint(msg)
          setBusy(false)
          return
        }
        throw err
      }
      const blocks = data.content ?? []

      const replyText = blocks
        .filter((b): b is AnthropicTextBlock => b.type === 'text')
        .map(b => b.text)
        .join('\n\n')
        .trim()

      // Apply any tool_use call from the model. Multiple tool_use blocks per
      // turn are rare but we handle them — last one wins for the same slice.
      let appliedToolName: string | undefined
      let toolError: string | undefined
      if (stepTool) {
        for (const block of blocks) {
          if (block.type !== 'tool_use' || block.name !== stepTool.tool.name) continue
          try {
            setDraft(stepTool.apply(draft, block.input))
            appliedToolName = block.name
          } catch (e) {
            toolError = e instanceof Error ? e.message : String(e)
          }
        }
      }

      const finalText = [
        replyText,
        appliedToolName && '✓ 已套用到右邊 canvas',
        toolError && `⚠️ Tool 套用失敗：${toolError}`,
      ].filter(Boolean).join('\n\n') || '(AI returned no text content.)'

      setMessages(m => [...m, { role: 'assistant', text: finalText, appliedTool: appliedToolName }])
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      setMessages(m => [...m, { role: 'assistant', text: `⚠️ 呼叫 AI 失敗：${msg}` }])
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid grid-cols-[380px_1fr] gap-3 h-[calc(100vh-220px)] min-h-[480px] max-h-[820px]">
      {/* Left — Chat */}
      <div className="flex min-h-0 flex-col rounded-md border border-rule bg-card">
        <div className="border-b border-rule bg-slate-50 px-3 py-2">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">Chat</p>
          <p className="text-xs text-ink-faint">{step.brief}</p>
        </div>

        <div ref={scrollRef} className="flex-1 min-h-0 overflow-y-auto p-3 space-y-3">
          {messages.map((m, i) => (
            <div key={i} className={cn('flex items-start gap-2', m.role === 'user' && 'flex-row-reverse')}>
              <div className={cn(
                'flex h-7 w-7 shrink-0 items-center justify-center rounded-full',
                m.role === 'assistant'
                  ? m.appliedTool ? 'bg-emerald-100 text-emerald-700' : 'bg-blue-100 text-blue-700'
                  : 'bg-slate-100 text-slate-700',
              )}>
                {m.role === 'assistant'
                  ? (m.appliedTool ? <Wand2 className="h-4 w-4" /> : <Bot className="h-4 w-4" />)
                  : <User className="h-4 w-4" />}
              </div>
              <div className={cn(
                'max-w-[320px] rounded-lg px-3 py-2 text-sm leading-snug',
                m.role === 'user' && 'whitespace-pre-wrap',
                m.role === 'assistant'
                  ? m.appliedTool ? 'bg-emerald-50 text-ink ring-1 ring-emerald-200' : 'bg-slate-50 text-ink'
                  : 'bg-primary text-white',
              )}>
                {m.role === 'assistant' ? <AssistantMarkdown text={m.text} /> : m.text}
              </div>
            </div>
          ))}
          {busy && (
            <div className="flex items-start gap-2">
              <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-blue-100 text-blue-700">
                <Bot className="h-4 w-4" />
              </div>
              <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-ink-muted italic">
                AI 思考中…{elapsed > 0 && <span className="ml-1 not-italic font-mono text-[11px] text-ink-faint">{elapsed}s</span>}
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
            <textarea
              value={input}
              onChange={e => setInput(e.target.value)}
              onKeyDown={e => {
                // Skip while the IME is mid-composition — pressing Enter
                // to pick a Chinese candidate must not send the message.
                // Safari sometimes drops `isComposing` and only sets
                // keyCode 229 on the underlying native event; check both.
                if (e.key !== 'Enter' || e.shiftKey) return
                if (e.nativeEvent.isComposing || e.nativeEvent.keyCode === 229) return
                e.preventDefault()
                send()
              }}
              placeholder={busy ? '送出中…' : '跟 AI 說明這個 step…(Shift+Enter 換行)'}
              disabled={busy}
              rows={1}
              // `field-sizing: content` lets the textarea grow with its
              // content up to max-h-32 (~8 rows); on older browsers it
              // falls back to the fixed `rows={1}` height. Chrome 123+
              // and Safari TP support this natively today.
              style={{ fieldSizing: 'content' } as React.CSSProperties}
              className="min-h-8 max-h-32 flex-1 resize-none rounded-md border border-rule bg-white px-3 py-1.5 text-sm leading-5 focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary disabled:bg-slate-100"
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
            POST /api/chat → Anthropic Claude Sonnet 4.6{STEP_TOOLS[step.id] ? ` · 工具：${STEP_TOOLS[step.id]!.tool.name}` : ''}
          </p>
        </div>
      </div>

      {/* Right — Canvas */}
      <div className="flex min-h-0 flex-col rounded-md border border-rule bg-card overflow-hidden">
        <div className="border-b border-rule bg-slate-50 px-3 py-2 flex items-center justify-between">
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-wider text-ink-muted">Canvas — {step.en}</p>
            <p className="text-xs text-ink-faint">由 AI 即時生成的問卷 / 預覽，您也可以直接在這裡編輯</p>
          </div>
          <span className="rounded bg-blue-50 px-2 py-0.5 text-[10px] font-mono uppercase tracking-wider text-blue-700">
            spec.{step.id}
          </span>
        </div>
        <div className="flex-1 min-h-0 overflow-y-auto p-4">
          {canvas}
        </div>
      </div>
    </div>
  )
}
