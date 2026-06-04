import { api } from '@/flowcook/api'
import {
  getDeployedFlowCodes,
  registerShippedFlows,
  type RegisterShippedResult,
} from '@/flowcook/api/shippedFlows'

/** Counts returned by the bpm-svc factory wipe. */
export interface FactoryWipeSummary {
  instancesDeleted: number
  tasksDeleted: number
  historyRowsDeleted: number
  capturedMessagesDeleted: number
  casesDeleted: number
}

export interface ReseedResult {
  ok: boolean
  users: number
  roles: number
}

/** Step 1 — wipe ALL bpm-owned runtime data (cases / tasks / notifications /
 *  captured mail / audits). bpm-svc, not gated behind the sandbox toggle. */
export const factoryWipeRuntime = () =>
  api<FactoryWipeSummary>('/bpmsvc/api/sandbox-admin/factory-reset', { method: 'POST' })

/** Step 2 — clear + reseed admin-owned identity (13 users / 6 depts / 1 group
 *  / 15 roles + grants). admin-svc. */
export const reseedIdentity = () =>
  api<ReseedResult>('/api/admin/reset/reseed', { method: 'POST' })

/** Step 3 — re-register every deployed flow into Admin_Flows (→ Published). */
export const reregisterFlows = async (): Promise<RegisterShippedResult> => {
  const flows = await getDeployedFlowCodes()
  return registerShippedFlows(flows)
}
