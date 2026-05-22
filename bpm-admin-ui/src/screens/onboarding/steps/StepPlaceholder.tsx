import { Construction } from 'lucide-react'
import { ONBOARDING_STEPS, type DraftSpec } from '@/lib/onboarding'

export function StepPlaceholder({ stepId, draft }: { stepId: string; draft: DraftSpec }) {
  const step = ONBOARDING_STEPS.find(s => s.id === stepId)
  return (
    <div className="flex flex-col items-center justify-center py-16 text-center">
      <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-amber-100 text-amber-700">
        <Construction className="h-7 w-7" />
      </div>
      <h3 className="mb-1 text-base font-semibold text-ink">
        {step?.en} {step?.zh && `· ${step.zh}`}
      </h3>
      <p className="max-w-md text-xs text-ink-muted leading-relaxed">
        {step?.brief}
      </p>
      <div className="mt-6 rounded border border-rule bg-slate-50 px-4 py-2.5 text-[11px] text-ink-muted">
        <p className="font-medium">Phase A 進度：</p>
        <p className="mt-0.5">SOURCE / STRUCTURE / FORMS / GO LIVE 已實作；其他 step 是 placeholder。</p>
        <p className="mt-1">目前 spec 累積：{draft.flow.nodes.length} 節點 · {draft.userTasks.length} user task · {draft.approvals.length} approval · {draft.testCases.length} test case</p>
      </div>
    </div>
  )
}
