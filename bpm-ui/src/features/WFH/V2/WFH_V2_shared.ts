import type { WFH_V2_Status, WFH_V2_SubmitPayload } from './WFH_V2_types'

export function emptyPayload(): WFH_V2_SubmitPayload {
  return { applyDate: '', start: '', end: '', reason: '', attachmentFileId: null }
}

/**
 * Senior approval (上級主管) kicks in at this many consecutive days
 * **or more** (spec gateway_days: `days >= 15`). V2 raised V1's > 7 gate.
 */
export const DAYS_GATE_THRESHOLD = 15

/** Whether a request needs the senior (上級主管) stage — mirrors the C# gate `c.Days >= 15`. */
export function needsSenior(days: number): boolean {
  return days >= DAYS_GATE_THRESHOLD
}

/**
 * Consecutive calendar days between start and end, inclusive — the natural
 * reading of the gateway's "連續日期". MUST stay in lockstep with the C# side
 * `WFH_V2_WfhService.ComputeConsecutiveDays`; the server recomputes and
 * overrides any client value at submit time.
 */
export function consecutiveDays(startIso: string, endIso: string): number {
  if (!startIso || !endIso) return 0
  const s = new Date(startIso)
  const e = new Date(endIso)
  if (Number.isNaN(s.getTime()) || Number.isNaN(e.getTime())) return 0
  if (s.getTime() > e.getTime()) return 0
  const ms = e.getTime() - s.getTime()
  return Math.round(ms / 86_400_000) + 1
}

export function zhStatus(s: WFH_V2_Status): string {
  switch (s) {
    case 'PendingManager':   return '待主管核准'
    case 'PendingSenior':    return '待上級主管核准'
    case 'ResubmitRequired': return '退回補件'
    case 'Completed':        return '已核准'
    case 'Cancelled':        return '已撤回'
  }
}
