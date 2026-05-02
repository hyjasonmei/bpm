import { useEffect, useRef, useState } from 'react'
import { Send, Bot, User } from 'lucide-react'
import { cn } from '@/lib/cn'
import type { OnboardingStep, DraftSpec } from '@/lib/onboarding'

/**
 * Co-Pilot Canvas — split layout (chat left, AI-generated canvas right).
 *
 * Phase A: chat is a *scripted* mock per step; messages don't actually call
 * Claude API yet. The point right now is to demo the interaction shape so the
 * partner can show customers what the experience feels like. Phase B will
 * wire this to the real Claude API + spec patcher.
 */

interface ChatMessage {
  role: 'ai' | 'user'
  text: string
}

const SCRIPTED_GREETINGS: Record<string, string> = {
  source:    '請描述您要設計的流程，或是上傳 PPT / Visio / 手繪 / Excel。也可以選一個既有範本（如「請假」）開始。',
  structure: '看一下右邊的 BPMN 骨架——節點、邊都對嗎？我有標出信心較低的節點，您可以點擊修改。',
  forms:     '右邊列出每個 user task 的欄位。請確認欄位、必填、條件規則。如果想加新欄位告訴我，我會更新右邊。',
  decisions: '每個 gateway 我都列在右邊。請告訴我每個 gateway 的條件——譬如「金額 > 50K 走 A 路徑」。',
  approvers: '請告訴我每個 approval 步驟由誰簽。我可以查您 AD（如果接了 MCP）找出對應的人。',
  notify:    '預設我先給您雙語的 email 模板，您可以在右邊微調文字、變數、收件人。',
  sla:       '建議：審核 24 工時、超時 escalation 通知。要不要改？',
  test:      '我用您的 spec 模擬了 3 張案件，請看右邊的測試結果。如果路徑不如預期，告訴我哪裡需要調整。',
  go_live:   '所有 validator 都過了。確認 spec 摘要正確嗎？按下「Submit Spec」就送到後台。',
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
  const scrollRef = useRef<HTMLDivElement>(null)

  // Reset chat when step changes; seed with scripted greeting.
  useEffect(() => {
    setMessages([{ role: 'ai', text: SCRIPTED_GREETINGS[step.id] ?? '請開始這一步。' }])
  }, [step.id])

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  const send = () => {
    const text = input.trim()
    if (!text) return
    setMessages(m => [
      ...m,
      { role: 'user', text },
      { role: 'ai', text: scriptedReply(step.id, text, draft) },
    ])
    setInput('')
  }

  // Suppress unused linter warning — `setDraft` is intentionally available for
  // future use when chat actually mutates the spec.
  void setDraft

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
                m.role === 'ai' ? 'bg-blue-100 text-blue-700' : 'bg-slate-100 text-slate-700',
              )}>
                {m.role === 'ai' ? <Bot className="h-4 w-4" /> : <User className="h-4 w-4" />}
              </div>
              <div className={cn(
                'max-w-[280px] rounded-lg px-3 py-2 text-sm leading-snug',
                m.role === 'ai' ? 'bg-slate-50 text-ink' : 'bg-primary text-white',
              )}>
                {m.text}
              </div>
            </div>
          ))}
        </div>

        <div className="border-t border-rule bg-slate-50 p-2">
          <div className="flex items-center gap-1.5">
            <input
              value={input}
              onChange={e => setInput(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send() } }}
              placeholder="跟 AI 說明這個 step…"
              className="h-8 flex-1 rounded-md border border-rule bg-white px-3 text-sm focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
            />
            <button
              onClick={send}
              className="flex h-8 w-8 items-center justify-center rounded-md bg-primary text-white hover:bg-blue-700"
            >
              <Send className="h-4 w-4" />
            </button>
          </div>
          <p className="mt-1 text-[10px] text-ink-faint">
            Phase A：scripted demo（沒接 Claude API）。Phase B 才會即時跟 AI 對話。
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

function scriptedReply(stepId: string, _userText: string, _draft: DraftSpec): string {
  // Very simple keyword echoes for demo purposes. Phase B replaces this.
  const lower = _userText.toLowerCase()
  if (lower.includes('請假') || lower.includes('leave')) {
    return '已套用「請假」範本到右邊 canvas。您可以接著在右邊微調或繼續對話。（Phase A：實際的範本載入請按 canvas 上的 Load Preset 按鈕）'
  }
  if (lower.includes('幫') || lower.includes('help')) {
    return `這一步：${stepId}。請看右邊 canvas，那是 spec 在這個 step 的可視化。任何想改的都可以在 canvas 上直接動，或在這裡描述需求。`
  }
  return `（Phase A scripted）已收到「${_userText}」。Phase B 接 Claude API 後會根據您的描述更新右邊 canvas。`
}
