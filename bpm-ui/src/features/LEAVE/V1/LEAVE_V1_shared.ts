/** Spec-aligned leave types — mirrors task_apply.fields[leave_type].options. */
export const LEAVE_TYPES = ['特休', '病假', '事假', '公假'] as const

/** Cert is required when leave_type is 病假 or 公假 (spec: task_apply.fields[cert].conditional). */
export function certRequired(leaveType: string): boolean {
  return leaveType === '病假' || leaveType === '公假'
}

/**
 * Business days between start and end (inclusive), Mon-Fri only.
 * Must stay in lockstep with the C# side `LEAVE_V1_LeaveService.ComputeBusinessDays`
 * — the server recomputes and overrides any client-side value at submit time.
 */
export function businessDaysBetween(startIso: string, endIso: string): number {
  if (!startIso || !endIso) return 0
  const s = new Date(startIso)
  const e = new Date(endIso)
  if (Number.isNaN(s.getTime()) || Number.isNaN(e.getTime())) return 0
  if (s.getTime() > e.getTime()) return 0
  let count = 0
  const cur = new Date(s)
  while (cur.getTime() <= e.getTime()) {
    const wd = cur.getDay()
    if (wd !== 0 && wd !== 6) count++
    cur.setDate(cur.getDate() + 1)
  }
  return count
}
