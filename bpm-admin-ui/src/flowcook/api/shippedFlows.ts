import { api } from '@/flowcook/api'

export interface DeployedFlow {
  flowCode: string
  displayName: string
  /** Highest deployed runtime version for this code (defaults to 1 if an older
   *  bpm-svc omits the field). Drives version-aware register-shipped: a newer
   *  deployed version publishes a new flow row and retires the prior one. */
  version?: number
}

export interface RegisterShippedResult {
  registered: string[]
  skipped: string[]
}

/** Flow codes whose runtime code is deployed on bpm-svc (its *_V1_Case
 *  tables). Comes from bpm-svc via the /bpmsvc dev proxy. */
export const getDeployedFlowCodes = () => api<DeployedFlow[]>('/bpmsvc/api/flow-codes')

/** Backfill these into Admin_Flows as Approved (admin-svc, idempotent). */
export const registerShippedFlows = (flows: DeployedFlow[]) =>
  api<RegisterShippedResult>('/api/flows/register-shipped', { method: 'POST', json: { flows } })
