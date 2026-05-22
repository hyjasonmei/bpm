/**
 * PR-K4 helper. Resolves the latest spec snapshot for a flowCode and
 * funnels it through the wizard's `migrateDraft` so the result is ready to
 * feed `<BpmnDiagram draft={…} />`. We always go via
 * <code>/api/admin/process-admin/definitions/{flowCode}/spec</code> (which
 * already prefers the bundle blob, then falls back to the filesystem
 * spec); LiveCaseDetail doesn't need to care about that resolution order.
 */

import { getSpecJson } from './processAdmin'
import { migrateDraft } from '@/lib/onboarding'
import type { DraftSpec } from '@/lib/onboarding'

export async function specJsonToDraft(flowCode: string): Promise<DraftSpec> {
  const text = await getSpecJson(flowCode)
  const parsed = JSON.parse(text) as unknown
  return migrateDraft(parsed)
}
