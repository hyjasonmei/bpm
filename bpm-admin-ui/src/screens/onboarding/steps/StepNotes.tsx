import type { DraftSpec } from '@/lib/onboarding'

interface Props {
  draft: DraftSpec
  setDraft: (d: DraftSpec) => void
}

/**
 * Step 11 — NOTES.
 *
 * Free-form notes shown to chef + reviewer + the lifecycle on-hold
 * pipeline (chef appends questions here when stuck; the user edits in
 * answers and clicks Resume).
 */
export function StepNotes({ draft, setDraft }: Props) {
  return (
    <div className="space-y-3">
      <div>
        <h3 className="text-sm font-semibold text-ink">補充備註 · Notes</h3>
        <p className="mt-1 text-xs text-ink-muted">
          給 chef / 驗收者看的補充說明。chef 在烹飪流程中如遇 spec 模糊會把問題加進這裡並把狀態切到 On Hold；
          您看到問題後直接在這裡回答、再按 Resume，chef 會重新從佇列拿起。
        </p>
      </div>
      <textarea
        value={draft.notes ?? ''}
        onChange={(e) => setDraft({ ...draft, notes: e.target.value })}
        rows={14}
        placeholder="（選填）特殊邏輯、邊界 case、合規要求……"
        className="block w-full rounded border border-rule bg-white px-3 py-2 text-sm text-ink outline-none placeholder:text-ink-faint focus:border-primary focus:ring-2 focus:ring-primary/20"
      />
    </div>
  )
}
