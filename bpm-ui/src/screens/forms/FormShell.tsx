import { useState } from 'react'
import { Workflow as WorkflowIcon, ExternalLink, Printer, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { SectionCard } from '@/components/ui/card'
import { Stepper } from '@/components/Stepper'
import { BpmnView } from '@/components/BpmnView'
import { lookupForm } from '@/features/registry'
import { Textarea } from '@/components/ui/form'
import { FORMS, type FormCode } from '@/lib/workflow'
import { PERSONAS, type PersonaCode } from '@/lib/role'
import type { NodeKind } from '@/types/process'

/** Mode the form is operating in. Default 'create' preserves the current UX. */
export type FormMode = 'create' | 'task'

interface FormShellProps {
  code: FormCode
  activeStep: number
  /** Reserved for future use (e.g. runtime-driven step transitions). Forms
   *  no longer drive step changes from the header — runtime owns step state. */
  setActiveStep?: (n: number) => void
  persona: PersonaCode
  /** info row content — overrides default Requestor / Cost Center / Business Unit */
  infoRow?: React.ReactNode
  copySelector?: boolean
  rightActions?: React.ReactNode
  /** 'create' is the default; 'task' renders read-only summary forms with
   *  runtime ActionBar buttons (Approve / Reject / Return). */
  mode?: FormMode
  children: React.ReactNode
}

export function FormShell({
  code, activeStep, persona,
  infoRow, copySelector = true, rightActions, children,
  mode = 'create',
}: FormShellProps) {
  const def = FORMS[code]
  const [bpmnOpen, setBpmnOpen] = useState(false)

  return (
    // pb-24 leaves clearance for the viewport-fixed ActionFooter that
    // create-mode forms render as their submit bar (portaled to <body>).
    <div className="space-y-4 pb-24">
      {/* Stepper bar — sits below the global header's form sub-header */}
      <SectionCard className="!p-0">
        <div className="flex items-center justify-between border-b border-rule bg-slate-50 px-4 py-2">
          <Stepper steps={def.steps} activeStep={activeStep} withZh />
          <Button variant="outline" size="xs" onClick={() => setBpmnOpen(true)}>
            <WorkflowIcon className="h-3 w-3" /> View BPMN
          </Button>
        </div>

        <div className="px-5 py-4">
          {/* Title + copy selector / read-only actions */}
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h1 className="text-xl font-bold text-ink">{def.label}</h1>
              <p className="text-[11px] uppercase tracking-wider text-ink-muted">{def.zhLabel}</p>
            </div>
            <div className="flex items-center gap-2">
              {rightActions}
              {copySelector && mode === 'create' && (
                <select className="h-8 rounded-md border border-rule bg-white px-3 text-sm text-ink-muted">
                  <option>Copy from my existing requests</option>
                </select>
              )}
              <Button variant="ghost" size="sm" title="Share permanent link"><ExternalLink className="h-3.5 w-3.5" /></Button>
              <Button variant="ghost" size="sm" title="Print"><Printer className="h-3.5 w-3.5" /></Button>
            </div>
          </div>

          {/* Info row */}
          {infoRow ?? <DefaultInfoRow persona={persona} />}
        </div>
      </SectionCard>

      {/* Form body */}
      {children}

      <BpmnView
        open={bpmnOpen}
        steps={def.steps}
        activeStep={activeStep}
        ownerByStep={def.ownerByStep}
        formLabel={`${def.code} — ${def.label}`}
        onClose={() => setBpmnOpen(false)}
        bpmnXml={lookupForm(def.code)?.bpmnXml}
      />
    </div>
  )
}

function DefaultInfoRow({ persona }: { persona: PersonaCode }) {
  const u = PERSONAS[persona].user
  return (
    <SectionCard className="mt-4">
      <div className="grid grid-cols-2 divide-x divide-rule">
        <div className="grid grid-cols-[110px_1fr] gap-x-4 gap-y-1 p-3 text-sm">
          <span className="text-ink-muted">Requestor</span>
          <span className="font-medium text-ink">{u.name}</span>
          <span className="text-ink-muted">Cost Center</span>
          <span className="text-ink">{u.dept}</span>
        </div>
        <div className="grid grid-cols-[110px_1fr] gap-x-4 gap-y-1 p-3 text-sm">
          <span className="text-ink-muted">Business Unit</span>
          <span className="text-ink">Taiwan (Taipei)</span>
          <span className="text-ink-muted">Persona</span>
          <span className="text-ink">{PERSONAS[persona].displayName} · {PERSONAS[persona].zhName}</span>
        </div>
      </div>
    </SectionCard>
  )
}

interface ActionBarProps {
  code: FormCode
  activeStep: number
  persona: PersonaCode
  /** 'create' (default) keeps today's submit-only UX; 'task' renders Approve/Reject/Return. */
  mode?: FormMode
  /** Required when mode === 'task'; controls whether Approve/Reject (Approval) or Submit (UserTask) is shown. */
  nodeKind?: NodeKind
  /** True while a runtime call is in-flight; disables buttons + shows spinner. */
  pending?: boolean
  onSubmit?: () => void
  onApprove?: (comment: string) => void
  onReject?: (comment: string) => void
  onReturnTask?: (comment: string) => void
  onClose?: () => void
}

/** Bottom action bar — runtime-driven task mode only. Legacy create-mode
 *  branches (persona-aware "View only" / "Copy to New") were cut along
 *  with the Reference_* demo forms; chef-cooked forms render their own
 *  create-mode submit bar with their own validation + confirm dialog. */
export function ActionBar({
  nodeKind, pending = false,
  onSubmit, onApprove, onReject, onReturnTask,
}: ActionBarProps) {
  return (
    <TaskActionBar
      nodeKind={nodeKind}
      pending={pending}
      onApprove={onApprove}
      onReject={onReject}
      onReturnTask={onReturnTask}
      onSubmit={onSubmit}
    />
  )

}

/** Comment-collecting modal used for Reject / Return flows. */
function CommentDialog({
  open, title, description, confirmText, tone, requireComment, onCancel, onConfirm,
}: {
  open: boolean
  title: string
  description?: string
  confirmText: string
  tone: 'danger' | 'default'
  requireComment: boolean
  onCancel: () => void
  onConfirm: (comment: string) => void
}) {
  const [comment, setComment] = useState('')
  const [touched, setTouched] = useState(false)

  if (!open) return null

  const empty = !comment.trim()
  const blockSubmit = requireComment && empty

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4" onClick={onCancel}>
      <div className="w-full max-w-md rounded-lg bg-white shadow-2xl" onClick={e => e.stopPropagation()} role="dialog" aria-modal="true">
        <div className="p-6">
          <h3 className="text-base font-bold text-ink">{title}</h3>
          {description && <p className="mt-2 text-sm leading-relaxed text-slate-600">{description}</p>}
          <div className="mt-4">
            <label className="mb-1 block text-sm font-medium text-ink">
              Comment {requireComment ? <span className="text-danger">*</span> : <span className="text-ink-faint">(optional)</span>}
            </label>
            <Textarea rows={3} value={comment} onChange={e => { setComment(e.target.value); setTouched(true) }} />
            {touched && blockSubmit && (
              <p className="mt-1 text-xs text-danger">Comment is required.</p>
            )}
          </div>
        </div>
        <div className="flex justify-end gap-2 rounded-b-lg border-t border-rule bg-slate-50/50 px-6 py-3">
          <Button variant="ghost" onClick={() => { setComment(''); setTouched(false); onCancel() }}>Cancel</Button>
          <Button
            variant={tone === 'danger' ? 'destructive' : 'primary'}
            disabled={blockSubmit}
            onClick={() => {
              if (blockSubmit) { setTouched(true); return }
              const value = comment
              setComment(''); setTouched(false)
              onConfirm(value)
            }}
          >
            {confirmText}
          </Button>
        </div>
      </div>
    </div>
  )
}

function TaskActionBar({
  nodeKind, pending, onApprove, onReject, onReturnTask, onSubmit,
}: {
  nodeKind?: NodeKind
  pending: boolean
  onApprove?: (comment: string) => void
  onReject?: (comment: string) => void
  onReturnTask?: (comment: string) => void
  onSubmit?: () => void
}) {
  // openKind: which dialog (if any) is currently open.
  const [openKind, setOpenKind] = useState<null | 'approve' | 'reject' | 'return'>(null)

  // For UserTask kind, show Submit + Return only (execution, not decision).
  // For Approval kind (default), show Approve + Reject + Return.
  const isUserTask = nodeKind === 'UserTask'

  return (
    <SectionCard>
      <div className="flex items-center justify-end gap-2 px-4 py-3">
        <Button variant="outline" size="md" disabled={pending} onClick={() => setOpenKind('return')}>Return</Button>
        {isUserTask ? (
          <Button variant="primary" size="md" disabled={pending} onClick={onSubmit}>
            {pending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Submit
          </Button>
        ) : (
          <>
            <Button variant="destructive" size="md" disabled={pending} onClick={() => setOpenKind('reject')}>Reject</Button>
            <Button variant="primary" size="md" disabled={pending} onClick={() => setOpenKind('approve')}>
              {pending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Approve
            </Button>
          </>
        )}
      </div>

      <CommentDialog
        open={openKind === 'approve'}
        title="Approve this step?"
        description="An optional comment will be recorded in the task history."
        confirmText="Confirm approve"
        tone="default"
        requireComment={false}
        onCancel={() => setOpenKind(null)}
        onConfirm={c => { setOpenKind(null); onApprove?.(c) }}
      />
      <CommentDialog
        open={openKind === 'reject'}
        title="Reject this step?"
        description="The instance will end in Errored state. A comment is required."
        confirmText="Confirm reject"
        tone="danger"
        requireComment={true}
        onCancel={() => setOpenKind(null)}
        onConfirm={c => { setOpenKind(null); onReject?.(c) }}
      />
      <CommentDialog
        open={openKind === 'return'}
        title="Return to previous step?"
        description="The previous owner will be re-assigned to revise. A comment is required."
        confirmText="Confirm return"
        tone="danger"
        requireComment={true}
        onCancel={() => setOpenKind(null)}
        onConfirm={c => { setOpenKind(null); onReturnTask?.(c) }}
      />
    </SectionCard>
  )
}
