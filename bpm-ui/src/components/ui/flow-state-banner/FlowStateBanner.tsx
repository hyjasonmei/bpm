import { useFlowRegistry, latestPerCode } from '@/hooks/useFlowRegistry'
import { InfoBanner } from '@/components/ui/form'

interface Props {
  flowCode: string
  /** Current case's flow version. When admin retires a later version
   *  this banner stays silent, since this specific case still belongs
   *  to a still-Approved version. */
  flowVersion: number
}

/**
 * Render-prop banner that warns the viewer when the case's flow has
 * been retired by an admin. Existing cases keep going (the retire
 * action doesn't touch in-flight workflows) but no new instances
 * can be launched.
 *
 * Per-flow chef-cooked CaseDetail components should mount this near
 * the top of the page (above the field grid, below the header).
 * Renders nothing while the registry is loading, or when the
 * matching flow row is still Approved.
 */
export function FlowStateBanner({ flowCode, flowVersion }: Props) {
  const { entries } = useFlowRegistry()
  if (!entries) return null
  const latest = latestPerCode(entries)
  const entry = latest.get(flowCode)
  if (!entry) return null
  if (entry.state !== 'Retired') return null
  // Only flag when the case's exact version was retired; if a newer
  // approved version exists, point users at it.
  // `no-print`: this is a screen advisory about the flow's lifecycle, not
  // part of the case record — it stays off the printed case sheet. Wrapped
  // rather than given to InfoBanner as a prop, since InfoBanner is a shared
  // primitive with several other callers.
  if (entry.version !== flowVersion) {
    return (
      <div className="no-print">
        <InfoBanner>
          此案件使用的 <span className="font-mono">{flowCode} v{flowVersion}</span> 已下架。最新版本為 v{entry.version}（state = {entry.state}）。新案件請使用最新版。
        </InfoBanner>
      </div>
    )
  }
  return (
    <div className="no-print">
      <InfoBanner>
        此流程 <span className="font-mono">{flowCode} v{flowVersion}</span> 已下架，新案件無法建立。本案件仍可繼續處理至結束。
      </InfoBanner>
    </div>
  )
}
