import { useEffect, useRef, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import {
  Bot,
  CheckCheck,
  ChefHat,
  Eye,
  FileCode,
  FileDiff,
  FlaskConical,
  HelpCircle,
  MessageSquareWarning,
  Send,
  Sparkles,
  User as UserIcon,
} from 'lucide-react'
import { cn } from '@/lib/cn'
import type { FlowState } from '@/flowcook/api/flows'
import type {
  ChatMessage,
  ChefArtifact,
  ChatMessageKind,
} from './types'
import { cookedVersionLabel } from './types'

// FE-only POC: Cook tab inside the AI Kitchen wizard. State + chat
// history are mocked locally on WizardView; this component is just
// the chat surface + simulate-chef demo controls. When the real
// chef pipeline lands, replace `simulate*` callbacks with API calls.

export function CookPanel({
  flowVersion,
  state,
  messages,
  cookedCount,
  onStateChange,
  onCookedCountChange,
  onMessagesChange,
}: {
  flowVersion: number
  state: FlowState
  messages: ChatMessage[]
  cookedCount: number
  onStateChange: (s: FlowState) => void
  onCookedCountChange: (n: number) => void
  onMessagesChange: (next: ChatMessage[]) => void
}) {
  const [input, setInput] = useState('')
  const scrollRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    el.scrollTo({ top: el.scrollHeight, behavior: 'smooth' })
  }, [messages.length])

  const isOnHold = state === 'OnHold'
  const isCompleted = state === 'Committed'
  const canType = isOnHold || isCompleted

  function pushMessage(msg: ChatMessage) {
    onMessagesChange([...messages, msg])
  }

  function send() {
    const text = input.trim()
    if (!text) return
    const kind: ChatMessageKind = isCompleted ? 'issue' : 'reply'
    pushMessage({
      id: `m-${Date.now()}`,
      sender: 'user',
      kind,
      content: text,
      timestamp: new Date().toISOString(),
    })
    setInput('')
    if (isCompleted) {
      // Completed → OnHold (issue opened, chef will pick up)
      onStateChange('OnHold')
    } else if (isOnHold) {
      // OnHold → Cooking (chef resumes with the reply)
      onStateChange('Cooking')
    }
  }

  function simulateStartCooking() {
    onStateChange('Cooking')
    pushMessage({
      id: `m-${Date.now()}`,
      sender: 'chef',
      kind: 'memo',
      content:
        "Picking up the spec. I'll scaffold **Domain → Application → Persistence → Api → UI** in that order, then run the integration suite.",
      timestamp: new Date().toISOString(),
    })
  }

  function simulateAskQuestion() {
    onStateChange('OnHold')
    pushMessage({
      id: `m-${Date.now()}`,
      sender: 'chef',
      kind: 'question',
      content:
        "Need clarification on the **approver chain**:\n\n" +
        "- Spec says `approver: manager` but the sample org has dotted-line reports.\n" +
        "- Should I:\n" +
        "  1. Resolve to the *primary* manager only, or\n" +
        "  2. Fan out to **primary + dotted-line** in parallel?\n\n" +
        "Also — what's the SLA when the manager is on leave? (Currently I'd default to *escalate to skip-level after 24h*.)",
      timestamp: new Date().toISOString(),
      artifacts: [
        { kind: 'diff', label: 'Approver resolver (partial)', count: 1 },
      ],
    })
  }

  function simulateCompleteCook() {
    const nextCount = cookedCount + 1
    const version = `V${flowVersion}.${nextCount - 1}`
    pushMessage({
      id: `m-${Date.now()}`,
      sender: 'chef',
      kind: 'completion',
      content:
        `**${version} cooked.** ✓\n\n` +
        `Scaffolded under \`Features/<CODE>/V${flowVersion}/\` across Domain / Application / Persistence / Api / UI. ` +
        `All integration tests green. Ready for review — deploy to **DEV** to smoke-test.`,
      timestamp: new Date().toISOString(),
      version,
      artifacts: [
        { kind: 'preview', label: 'Form preview' },
        { kind: 'preview', label: 'Case detail' },
        { kind: 'files', label: 'Generated files', count: 14 },
        { kind: 'tests', label: 'Tests passing', count: 23 },
      ],
    })
    onCookedCountChange(nextCount)
    onStateChange('Committed')
  }

  function simulateResume() {
    onStateChange('Cooking')
    pushMessage({
      id: `m-${Date.now()}`,
      sender: 'chef',
      kind: 'memo',
      content: 'Got it — resuming. Will fold the change into the next cook.',
      timestamp: new Date().toISOString(),
    })
  }

  const latestVersion = cookedVersionLabel(flowVersion, cookedCount)

  return (
    <div className="flex h-full min-h-0 flex-col rounded-lg border border-rule bg-card shadow-sm">
      <header className="flex items-center justify-between gap-3 border-b border-rule px-5 py-3">
        <div className="flex items-center gap-2">
          <ChefHat className="h-4 w-4 text-primary" />
          <h3 className="text-sm font-semibold text-ink">Cook</h3>
          <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            chef ↔ user
          </span>
          {latestVersion && (
            <span className="ml-2 inline-flex items-center gap-1 rounded-full border border-good/30 bg-good/10 px-2 py-0.5 font-mono text-[10px] tracking-wide text-good">
              <CheckCheck className="h-3 w-3" />
              latest {latestVersion}
            </span>
          )}
        </div>
        <SimulateChefMenu
          state={state}
          onStart={simulateStartCooking}
          onAsk={simulateAskQuestion}
          onComplete={simulateCompleteCook}
          onResume={simulateResume}
        />
      </header>

      <div ref={scrollRef} className="flex-1 overflow-auto px-5 py-4">
        {messages.length === 0 ? (
          <EmptyState />
        ) : (
          <ul className="space-y-4">
            {messages.map((m) => <MessageBubble key={m.id} m={m} />)}
          </ul>
        )}
      </div>

      <div className="border-t border-rule px-5 py-3">
        {!canType ? (
          <ComposerHint state={state} />
        ) : (
          <form onSubmit={(e) => { e.preventDefault(); send() }}>
            <textarea
              value={input}
              onChange={(e) => setInput(e.target.value)}
              placeholder={
                isCompleted
                  ? 'Spotted something off? Open an issue — chef will re-cook.'
                  : 'Reply to chef…'
              }
              rows={3}
              className="block w-full resize-none rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
              onKeyDown={(e) => {
                if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
                  e.preventDefault()
                  send()
                }
              }}
            />
            <div className="mt-2 flex items-center justify-between">
              <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">
                ⌘/Ctrl + Enter to send
              </span>
              <button
                type="submit"
                disabled={!input.trim()}
                className={cn(
                  'inline-flex items-center gap-1.5 rounded px-3 py-1.5 text-xs font-semibold text-white transition-colors disabled:opacity-50',
                  isCompleted
                    ? 'bg-warn hover:bg-warn/90'
                    : 'bg-primary hover:bg-primary/90',
                )}
              >
                {isCompleted ? (
                  <>
                    <MessageSquareWarning className="h-3.5 w-3.5" />
                    Open issue
                  </>
                ) : (
                  <>
                    <Send className="h-3.5 w-3.5" />
                    Submit reply
                  </>
                )}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────
// Bits
// ──────────────────────────────────────────────────────────────

function EmptyState() {
  return (
    <div className="flex h-full items-center justify-center">
      <div className="max-w-sm text-center">
        <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-full bg-bg">
          <ChefHat className="h-6 w-6 text-ink-faint" />
        </div>
        <p className="text-sm text-ink">Chef hasn't started yet.</p>
        <p className="mt-1 text-xs text-ink-muted">
          Once chef picks up the spec, their memos, questions and completions land here.
          Use the <span className="font-mono text-ink">demo</span> menu (top right) to
          simulate chef behavior while the real pipeline is being wired.
        </p>
      </div>
    </div>
  )
}

function ComposerHint({ state }: { state: FlowState }) {
  if (state === 'Submitted') {
    return (
      <div className="flex items-center gap-2 text-xs text-ink-muted">
        <Sparkles className="h-3.5 w-3.5 text-accent" />
        <span>Waiting for chef to pick up the order.</span>
      </div>
    )
  }
  if (state === 'Cooking') {
    return (
      <div className="flex items-center gap-2 text-xs text-ink-muted">
        <ChefHat className="h-3.5 w-3.5 animate-pulse text-accent" />
        <span>Chef is cooking — sit tight. Questions and the final cook will appear above.</span>
      </div>
    )
  }
  return (
    <div className="text-xs text-ink-muted">No reply needed in this state.</div>
  )
}

function SimulateChefMenu({
  state,
  onStart,
  onAsk,
  onComplete,
  onResume,
}: {
  state: FlowState
  onStart: () => void
  onAsk: () => void
  onComplete: () => void
  onResume: () => void
}) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-faint">
        demo
      </span>
      {state === 'Submitted' && (
        <SimButton onClick={onStart} icon={<Bot className="h-3 w-3" />} label="Chef picks up" />
      )}
      {state === 'Cooking' && (
        <>
          <SimButton onClick={onAsk} icon={<HelpCircle className="h-3 w-3" />} label="Chef asks question" tone="warn" />
          <SimButton onClick={onComplete} icon={<CheckCheck className="h-3 w-3" />} label="Chef completes cook" tone="good" />
        </>
      )}
      {state === 'OnHold' && (
        <SimButton onClick={onResume} icon={<Bot className="h-3 w-3" />} label="Chef resumes" />
      )}
      {state === 'Committed' && (
        <span className="text-[11px] italic text-ink-faint">
          Open an issue to trigger a re-cook
        </span>
      )}
    </div>
  )
}

function SimButton({
  onClick,
  icon,
  label,
  tone = 'neutral',
}: {
  onClick: () => void
  icon: React.ReactNode
  label: string
  tone?: 'neutral' | 'warn' | 'good'
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'inline-flex items-center gap-1 rounded border px-2 py-0.5 font-mono text-[10px] tracking-wide transition-colors',
        tone === 'neutral' && 'border-rule bg-card text-ink-muted hover:border-primary hover:text-primary',
        tone === 'warn' && 'border-warn/40 bg-warn/5 text-warn hover:bg-warn/10',
        tone === 'good' && 'border-good/40 bg-good/5 text-good hover:bg-good/10',
      )}
    >
      {icon}
      {label}
    </button>
  )
}

function MessageBubble({ m }: { m: ChatMessage }) {
  if (m.sender === 'system' || m.kind === 'milestone') {
    return (
      <li className="flex items-center gap-3 py-1">
        <div className="flex-1 border-t border-rule" />
        <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
          {m.content}
        </span>
        <div className="flex-1 border-t border-rule" />
      </li>
    )
  }

  if (m.kind === 'completion') {
    return (
      <li className="rounded-lg border border-good/30 bg-good/5 p-4">
        <div className="mb-3 flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <div className="flex h-6 w-6 items-center justify-center rounded-full bg-good/15 text-good">
              <CheckCheck className="h-3.5 w-3.5" />
            </div>
            <span className="font-mono text-xs font-semibold tracking-wide text-good">
              {m.version} cooked
            </span>
          </div>
          <time className="font-mono text-[10px] text-ink-muted">
            {formatTime(m.timestamp)}
          </time>
        </div>
        <MarkdownBody source={m.content} />
        {m.artifacts && m.artifacts.length > 0 && (
          <div className="mt-3 grid grid-cols-2 gap-2">
            {m.artifacts.map((a, i) => <ArtifactChip key={i} a={a} />)}
          </div>
        )}
      </li>
    )
  }

  const isChef = m.sender === 'chef'

  return (
    <li className={cn('flex gap-3', isChef ? '' : 'flex-row-reverse')}>
      <div className={cn(
        'flex h-7 w-7 shrink-0 items-center justify-center rounded-full',
        isChef ? 'bg-primary/10 text-primary' : 'bg-accent/15 text-accent',
      )}>
        {isChef
          ? <ChefHat className="h-3.5 w-3.5" />
          : <UserIcon className="h-3.5 w-3.5" />}
      </div>
      <div className={cn('flex max-w-[80%] flex-col gap-1', isChef ? 'items-start' : 'items-end')}>
        <div className="flex items-center gap-2">
          <span className="font-mono text-[10px] tracking-[0.14em] uppercase text-ink-muted">
            {isChef ? 'chef' : 'you'} · {kindLabel(m.kind)}
          </span>
          <time className="font-mono text-[10px] text-ink-faint">
            {formatTime(m.timestamp)}
          </time>
        </div>
        <div className={cn(
          'rounded-lg px-3 py-2 text-sm',
          isChef
            ? m.kind === 'question'
              ? 'border border-warn/30 bg-warn/5 text-ink'
              : 'border border-rule bg-bg/60 text-ink'
            : m.kind === 'issue'
              ? 'border border-warn/30 bg-warn/10 text-ink'
              : 'border border-accent/30 bg-accent/10 text-ink',
        )}>
          {isChef
            ? <MarkdownBody source={m.content} />
            : <div className="whitespace-pre-wrap">{m.content}</div>}
        </div>
        {isChef && m.artifacts && m.artifacts.length > 0 && (
          <div className="mt-1 flex flex-wrap gap-1.5">
            {m.artifacts.map((a, i) => <ArtifactChip key={i} a={a} compact />)}
          </div>
        )}
      </div>
    </li>
  )
}

function ArtifactChip({ a, compact = false }: { a: ChefArtifact; compact?: boolean }) {
  const icon =
    a.kind === 'preview' ? <Eye className="h-3 w-3" /> :
    a.kind === 'files'   ? <FileCode className="h-3 w-3" /> :
    a.kind === 'tests'   ? <FlaskConical className="h-3 w-3" /> :
                           <FileDiff className="h-3 w-3" />
  if (compact) {
    return (
      <span className="inline-flex items-center gap-1 rounded border border-rule bg-card px-1.5 py-0.5 font-mono text-[10px] text-ink-muted">
        {icon}
        {a.label}{a.count != null ? ` · ${a.count}` : ''}
      </span>
    )
  }
  return (
    <button
      type="button"
      onClick={() => { /* POC: artifacts are mocks, no real link */ }}
      className="flex items-center gap-2 rounded border border-rule bg-card px-3 py-2 text-left text-xs transition-colors hover:border-primary hover:text-primary"
    >
      <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded bg-bg text-ink-muted">
        {icon}
      </span>
      <span className="flex flex-col">
        <span className="font-medium text-ink">{a.label}</span>
        {a.count != null && (
          <span className="font-mono text-[10px] text-ink-muted">{a.count}</span>
        )}
      </span>
    </button>
  )
}

function MarkdownBody({ source }: { source: string }) {
  return (
    <div className="space-y-1.5 text-sm leading-relaxed">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          p:  ({ children }) => <p className="my-1">{children}</p>,
          ul: ({ children }) => <ul className="my-1 list-disc space-y-0.5 pl-5">{children}</ul>,
          ol: ({ children }) => <ol className="my-1 list-decimal space-y-0.5 pl-5">{children}</ol>,
          li: ({ children }) => <li className="text-sm">{children}</li>,
          code: ({ children }) => <code className="rounded bg-bg px-1 py-0.5 font-mono text-[12px]">{children}</code>,
          strong: ({ children }) => <strong className="font-semibold text-ink">{children}</strong>,
          em: ({ children }) => <em className="italic">{children}</em>,
        }}
      >
        {source}
      </ReactMarkdown>
    </div>
  )
}

function kindLabel(k: ChatMessageKind): string {
  switch (k) {
    case 'memo': return 'memo'
    case 'question': return 'question'
    case 'completion': return 'completion'
    case 'reply': return 'reply'
    case 'issue': return 'issue'
    case 'milestone': return 'milestone'
  }
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}
