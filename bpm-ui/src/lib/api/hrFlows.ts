/*
 * `/api/hr-flows/*` was retired in Phase 1.3 — HrFlowsController and
 * IHrFlowService are deleted from bpm-svc. RESIGN / DEPTX flows go
 * through `ProcessRuntime` (`/api/processes`) the same as every other
 * flow now.
 *
 * The Reference_*Form.tsx components still import these helpers and
 * still compile, so we keep the function shapes in place but throw a
 * loud runtime error if anyone actually calls them. Reference forms
 * are no longer mounted in App.tsx and exist purely as chef's visual
 * reference — their data submission paths are intentionally dead.
 */
import type {
  HrFlowInstanceDto,
  HrFlowSpecCode,
  HrFlowSummaryDto,
} from '@/types/hrFlows'

function retired(name: string): never {
  throw new Error(
    `[hrFlows api retired] ${name} — HrFlowsController removed in Phase 1.3. ` +
    `RESIGN/DEPTX now go through /api/processes (ProcessRuntime). ` +
    `Reference_*Form.tsx are visual reference only; their submit paths are dead.`,
  )
}

export async function startHrFlow(_specCode: HrFlowSpecCode, _formData: unknown): Promise<HrFlowInstanceDto> {
  retired('startHrFlow')
}

export async function getHrFlow(_id: string): Promise<HrFlowInstanceDto> {
  retired('getHrFlow')
}

export async function getMyHrFlows(): Promise<HrFlowSummaryDto[]> {
  retired('getMyHrFlows')
}

export async function getMyHrFlowTodos(): Promise<HrFlowSummaryDto[]> {
  retired('getMyHrFlowTodos')
}

export async function approveHrFlow(_id: string, _comment?: string): Promise<HrFlowInstanceDto> {
  retired('approveHrFlow')
}

export async function returnHrFlow(_id: string, _comment: string): Promise<HrFlowInstanceDto> {
  retired('returnHrFlow')
}

export async function resubmitHrFlow(_id: string, _formData: unknown): Promise<HrFlowInstanceDto> {
  retired('resubmitHrFlow')
}

export async function cancelHrFlow(_id: string): Promise<HrFlowInstanceDto> {
  retired('cancelHrFlow')
}
