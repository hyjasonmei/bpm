// FE-only POC types for the Cook + Serve panels. Nothing here hits
// admin-svc — Cook chat history and Serve deployment cards live
// entirely in WizardView local state. Real wiring lands when the
// chef pipeline + serve runtime exist.

export type ChatSender = 'chef' | 'user' | 'system'

export type ChatMessageKind =
  | 'memo'        // chef: short status note ("picking up the spec")
  | 'question'    // chef: blocking question (markdown)
  | 'completion'  // chef: cook finished — carries version + artifacts
  | 'reply'       // user: reply to a chef question
  | 'issue'       // user: issue opened against a completed cook
  | 'milestone'   // system: divider (e.g. "submitted to chef")

export type ChefArtifactKind = 'preview' | 'files' | 'tests' | 'diff'

export interface ChefArtifact {
  kind: ChefArtifactKind
  label: string
  count?: number
}

export interface ChatMessage {
  id: string
  sender: ChatSender
  kind: ChatMessageKind
  content: string
  timestamp: string
  artifacts?: ChefArtifact[]
  version?: string  // 'V1.0' — set on completion messages
}

export type EnvId = 'DEV' | 'STG' | 'PRD'

export type DeployStatus = 'not-deployed' | 'deploying' | 'running' | 'off'

export interface DeployState {
  status: DeployStatus
  deployedVersion: string | null
}

export const DEFAULT_DEPLOYMENTS: Record<EnvId, DeployState> = {
  DEV: { status: 'not-deployed', deployedVersion: null },
  STG: { status: 'not-deployed', deployedVersion: null },
  PRD: { status: 'not-deployed', deployedVersion: null },
}

export function cookedVersionLabel(flowVersion: number, cookedCount: number): string | null {
  if (cookedCount <= 0) return null
  return `V${flowVersion}.${cookedCount - 1}`
}
