import { api } from '@/flowcook/api'

/** White-label branding, persisted on bpm-svc's shared TenantSettings.
 *  Images are small base64 data-URIs. */
export interface BrandingDto {
  systemName: string | null
  logoDataUri: string | null
  faviconDataUri: string | null
}

// Routed to bpm-svc (port 5290) via the `/bpmsvc` dev proxy — branding lives
// on the shared runtime DB, not admin-svc.
const BASE = '/bpmsvc/api/branding'

export const getBranding = () => api<BrandingDto>(BASE)

export const putBranding = (b: BrandingDto) =>
  api<BrandingDto>(BASE, { method: 'PUT', json: b })
