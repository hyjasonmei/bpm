import { api } from '@/flowcook/api'

/**
 * One environment's deploy config for the Publish→Deploy pipeline.
 *
 * These are Azure resource NAMES only — there are deliberately NO secrets
 * (no tokens / credentials). The deploy worker authenticates with its own
 * `az` login and fetches Static Web App deployment tokens at deploy time;
 * the names below just tell it WHICH resources to push to.
 *
 * Routed to bpm-admin-svc (the default `/api/...` base), authorized by the
 * normal admin JWT bearer.
 */
export interface DeployEnvConfig {
  /** Logical environment name (key for upsert), e.g. "poc" / "prod". */
  envName: string
  /** Azure resource group, e.g. "rg-poc". */
  resourceGroup: string
  /** bpm-svc App Service name, e.g. "poc-flowcook-api". */
  bpmSvcApp: string
  /** admin-svc App Service name, e.g. "poc-flowcook-admin-api". */
  adminSvcApp: string
  /** bpm-ui Static Web App name, e.g. "poc-flowcook-ui". */
  bpmUiSwa: string
  /** admin-ui Static Web App name, e.g. "poc-flowcook-admin-ui". */
  adminUiSwa: string
  /** Whether this env is eligible for deploys. */
  enabled: boolean
}

/** GET /api/site-setting/deploy-config → all configured envs. */
export const listDeployConfig = () =>
  api<DeployEnvConfig[]>('/api/site-setting/deploy-config')

/** PUT /api/site-setting/deploy-config → upsert one env (keyed by envName). */
export const upsertDeployConfig = (cfg: DeployEnvConfig) =>
  api<DeployEnvConfig>('/api/site-setting/deploy-config', { method: 'PUT', json: cfg })
