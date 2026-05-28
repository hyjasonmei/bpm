import { useCallback, useEffect, useRef, useState } from 'react'
import { listFlowMessages, type FlowChatMessageDto } from '@/flowcook/api/flowChat'
import type { ChatMessage, ChatMessageKind, ChatSender, ChefArtifact } from './types'

interface State {
  messages: ChatMessage[]
  loading: boolean
  error: Error | null
}

const POLL_MS = 30_000

/**
 * Cook tab chat thread. One source of truth — admin-svc. Polls every
 * 30s while the panel is mounted; callers also get a `refresh()` so
 * simulate / user-reply actions land instantly without waiting on the
 * next poll tick.
 */
export function useCookMessages(flowId: string | null): State & { refresh: () => Promise<void> } {
  const [state, setState] = useState<State>({ messages: [], loading: true, error: null })
  const inflight = useRef<boolean>(false)

  const refresh = useCallback(async () => {
    if (!flowId || inflight.current) return
    inflight.current = true
    try {
      const dtos = await listFlowMessages(flowId)
      setState({ messages: dtos.map(toChatMessage), loading: false, error: null })
    } catch (err) {
      setState(s => ({ ...s, loading: false, error: err instanceof Error ? err : new Error(String(err)) }))
    } finally {
      inflight.current = false
    }
  }, [flowId])

  useEffect(() => {
    if (!flowId) return
    void refresh()
    const t = window.setInterval(() => { void refresh() }, POLL_MS)
    return () => window.clearInterval(t)
  }, [flowId, refresh])

  return { ...state, refresh }
}

/** Server DTO → local ChatMessage. Lowercases the enum so the existing
 *  CookPanel renderer (which still uses 'memo' / 'question' / etc.)
 *  stays untouched. */
function toChatMessage(dto: FlowChatMessageDto): ChatMessage {
  return {
    id: dto.id,
    sender: dto.sender.toLowerCase() as ChatSender,
    kind: dto.kind.toLowerCase() as ChatMessageKind,
    content: dto.content,
    timestamp: dto.createdAt,
    version: dto.version ?? undefined,
    artifacts: parseArtifacts(dto.artifactsJson),
  }
}

function parseArtifacts(json: string | null): ChefArtifact[] | undefined {
  if (!json) return undefined
  try {
    const parsed = JSON.parse(json) as Record<string, unknown>
    const out: ChefArtifact[] = []
    if (typeof parsed.branch === 'string')        out.push({ kind: 'diff',    label: `Branch: ${parsed.branch}` })
    if (typeof parsed.fileCount === 'number')     out.push({ kind: 'files',   label: 'Generated files', count: parsed.fileCount })
    if (typeof parsed.testsPassing === 'number')  out.push({ kind: 'tests',   label: 'Tests passing',   count: parsed.testsPassing })
    if (typeof parsed.previewLabel === 'string')  out.push({ kind: 'preview', label: parsed.previewLabel })
    return out.length > 0 ? out : undefined
  } catch {
    return undefined
  }
}
