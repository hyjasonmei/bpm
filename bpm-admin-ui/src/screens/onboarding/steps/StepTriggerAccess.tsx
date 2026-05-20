import { useEffect } from 'react'
import { AlertTriangle, FileText } from 'lucide-react'
import { deriveAutoTrigger, type DraftSpec } from '@/lib/onboarding'
import { PrincipalPickerField } from '@/components/principal-picker/PrincipalPicker'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

/**
 * Step 2 — ACCESS.
 *
 * The form trigger is auto-derived from the first user task in the
 * flow (心智模型：「第一個 task = 送單」). The wizard surfaces it
 * read-only as confirmation; to change which form starts the flow the
 * user reorders nodes in SOURCE. Schema still writes `triggers[]` so
 * cron / webhook / mail can be added later without migration.
 *
 * The page itself is about ACCESS: who can launch this flow, and who
 * can watch other people's instances. visibleTo is auto-mirrored from
 * launchableBy (能啟動的自然看得到), so the UI shows only two pickers.
 *
 * Principal picker is the reusable PrincipalPickerField (modal with
 * USER / DEPT / GROUP / ROLE tabs + search + select buffer).
 */
export function StepTriggerAccess({ draft, setDraft }: Props) {
  const firstUtNode = draft.flow.nodes.find(n => n.type === 'userTask')
  const firstUt = firstUtNode ? draft.userTasks.find(t => t.id === firstUtNode.id) : undefined
  const autoTrigger = deriveAutoTrigger(draft)

  // Keep draft.triggers in sync with the auto-derived value so other
  // consumers (spec serializer, bundle builder) see the same trigger
  // without depending on this component being mounted.
  useEffect(() => {
    const current = draft.triggers[0]
    const same = autoTrigger
      && current
      && current.id === autoTrigger.id
      && current.type === autoTrigger.type
      && current.formCode === autoTrigger.formCode
    if (autoTrigger && !same) {
      setDraft({ ...draft, triggers: [autoTrigger] })
    } else if (!autoTrigger && draft.triggers.length > 0) {
      setDraft({ ...draft, triggers: [] })
    }
  }, [autoTrigger?.id, autoTrigger?.formCode]) // eslint-disable-line react-hooks/exhaustive-deps

  function setLaunchableBy(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, launchableBy: list, visibleTo: list } })
  }
  function setWatcher(list: string[]) {
    setDraft({ ...draft, access: { ...draft.access, watcher: list } })
  }

  return (
    <div className="space-y-6">
      <SectionHeading>送單表單 · Submit Form</SectionHeading>
      <TriggerSummary firstUtNode={firstUtNode} firstUtFormCode={firstUt?.formCode} autoTrigger={autoTrigger} />

      <SectionHeading>存取權限 · Access</SectionHeading>
      <PrincipalPickerField
        label="可啟動 — 誰能送這張表單啟動流程"
        helper="這些人也會在流程目錄裡看到本流程（能啟動的自然看得到）。"
        value={draft.access.launchableBy}
        onChange={setLaunchableBy}
        modalTitle="選擇可啟動流程的對象"
      />
      <PrincipalPickerField
        label="旁觀者 — 誰能看別人開出來的 instance"
        helper="業務監看用，譬如主管監看部門所有人的請假，或 HR 監看全公司。可空。"
        value={draft.access.watcher}
        onChange={setWatcher}
        modalTitle="選擇旁觀者"
      />
    </div>
  )
}

function SectionHeading({ children }: { children: React.ReactNode }) {
  return <h3 className="text-sm font-semibold text-ink">{children}</h3>
}

/** Auto-derived trigger summary. Three states:
 *  - success: shows form name + code in a calm info card
 *  - warning: first user task has no formCode → point to FORMS step
 *  - empty:   no user task at all → point to SOURCE step */
function TriggerSummary({
  firstUtNode, firstUtFormCode, autoTrigger,
}: {
  firstUtNode: { id: string; label: string } | undefined
  firstUtFormCode: string | undefined
  autoTrigger: { id: string; formCode: string } | null
}) {
  if (autoTrigger && firstUtNode) {
    return (
      <div className="rounded-md border border-rule bg-card p-4">
        <div className="flex items-start gap-3">
          <FileText className="mt-0.5 h-4 w-4 text-primary" />
          <div className="flex-1">
            <div className="text-sm font-medium text-ink">
              {firstUtNode.label}
              <span className="ml-2 font-mono text-xs tracking-wider text-ink-muted">{autoTrigger.formCode}</span>
            </div>
            <p className="mt-1 text-xs text-ink-muted">
              使用者送這張表單 = 啟動這個流程。<br />
              自動取自流程第一個 user task；要改送單表單請到 <span className="font-medium text-ink">SOURCE</span> 調整節點順序，或到 <span className="font-medium text-ink">FORMS</span> 改 form code。
            </p>
          </div>
        </div>
      </div>
    )
  }

  if (firstUtNode && !firstUtFormCode) {
    return (
      <div className="rounded-md border border-warn/30 bg-warn/5 p-4">
        <div className="flex items-start gap-3">
          <AlertTriangle className="mt-0.5 h-4 w-4 text-warn" />
          <div className="flex-1">
            <div className="text-sm font-medium text-ink">送單表單尚未指定 form code</div>
            <p className="mt-1 text-xs text-ink-muted">
              第一個 user task「{firstUtNode.label}」還沒設 form code，無法當送單表單。請到 <span className="font-medium text-ink">FORMS</span> 步驟補上。
            </p>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="rounded-md border border-warn/30 bg-warn/5 p-4">
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-4 w-4 text-warn" />
        <div className="flex-1">
          <div className="text-sm font-medium text-ink">流程還沒有送單表單</div>
          <p className="mt-1 text-xs text-ink-muted">
            這個流程沒有任何 user task，無法被使用者啟動。請到 <span className="font-medium text-ink">SOURCE</span> 加入至少一個 user task 作為送單表單。
          </p>
        </div>
      </div>
    </div>
  )
}
