/**
 * Flow Library REST client. Wraps the `/api/admin/flow-library` surface
 * shipped in PR-I5 (FlowLibraryController.cs).
 *
 * Style mirrors `lib/api/adminRoles.ts` and `lib/api/sandbox.ts`:
 *   - Each function returns a typed promise; non-2xx → throw with a string
 *     pulled from `body.detail` / `body.title` / `body.error` / fallback.
 *   - `apiFetch` injects the JWT bearer; for multipart we deliberately
 *     omit Content-Type so the browser supplies the boundary string.
 */

import { apiFetch, BPM_SVC_URL } from '@/lib/apiFetch'
import type {
  BundleManifest,
  FlowLibraryDetailDto,
  FlowLibraryItemDto,
  ImportDraftResult,
  ImportInstallResult,
  ReproReport,
  SampleOrgSnapshot,
  TestCaseSnapshot,
  BundleValidationResult,
  SpecBundleStatus,
} from '@/types/flowLibrary'

async function jsonOrThrow<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      if (body?.detail) detail = body.detail
      else if (body?.error) detail = body.error
      else if (body?.title) detail = body.title
    } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.json() as T
}

export async function listBundles(tenantCode: string = 'default'): Promise<FlowLibraryItemDto[]> {
  const res = await apiFetch(`/api/admin/flow-library?tenantCode=${encodeURIComponent(tenantCode)}`)
  return jsonOrThrow<FlowLibraryItemDto[]>(res)
}

export async function getBundleDetail(id: string): Promise<FlowLibraryDetailDto> {
  const res = await apiFetch(`/api/admin/flow-library/${id}`)
  return jsonOrThrow<FlowLibraryDetailDto>(res)
}

/**
 * Returns the absolute URL the browser can navigate to for a download.
 * Caller picks the trigger (`window.location = ...` or an anchor click);
 * we don't trigger it here so callers stay testable.
 *
 * NOTE: this URL hits the API directly without the bearer header. The
 * PR-I5 controller is gated by `[Authorize(Roles = "admin")]`, so a
 * `window.location` redirect will only succeed if the auth cookie/session
 * piggybacks. If the dev-mode setup ever stops handing out a browser-side
 * cookie we'll need to swap this for an anchor + fetch+blob dance.
 */
export function exportBundleUrl(id: string): string {
  return `${BPM_SVC_URL}/api/admin/flow-library/${id}/export`
}

/**
 * Trigger an export download via fetch + Blob URL. Carries the JWT bearer,
 * so it works under the standard auth flow without relying on a session
 * cookie. Caller should invoke this from a click handler.
 */
export async function downloadBundle(id: string): Promise<void> {
  const res = await apiFetch(`/api/admin/flow-library/${id}/export`)
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try { const body = await res.json(); if (body?.error) detail = body.error } catch { /* ignore */ }
    throw new Error(detail)
  }
  // Try to read the server's filename from Content-Disposition; fall back to id.
  const cd = res.headers.get('content-disposition') ?? ''
  const m = /filename\s*=\s*"?([^";]+)"?/i.exec(cd)
  const filename = m?.[1] ?? `bundle_${id}.zip`
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  a.remove()
  // Defer revoke a tick so the click has time to start the download.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export async function getBundleFile(id: string, path: string): Promise<Blob> {
  const res = await apiFetch(`/api/admin/flow-library/${id}/files/${encodeBundlePath(path)}`)
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try { const body = await res.json(); if (body?.error) detail = body.error } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.blob()
}

export async function getBundleFileText(id: string, path: string): Promise<string> {
  const res = await apiFetch(`/api/admin/flow-library/${id}/files/${encodeBundlePath(path)}`)
  if (!res.ok) {
    let detail = `${res.status} ${res.statusText}`
    try { const body = await res.json(); if (body?.error) detail = body.error } catch { /* ignore */ }
    throw new Error(detail)
  }
  return await res.text()
}

export async function getBundleFileJson<T = unknown>(id: string, path: string): Promise<T> {
  const text = await getBundleFileText(id, path)
  return JSON.parse(text) as T
}

/**
 * Encode each segment but keep slashes as-is — the controller's catch-all
 * route `/files/{*path}` accepts forward-slashes verbatim.
 */
function encodeBundlePath(path: string): string {
  return path.split('/').map(encodeURIComponent).join('/')
}

export async function importBundleInstall(file: File): Promise<ImportInstallResult> {
  const fd = new FormData()
  fd.append('file', file)
  // Don't set Content-Type — the browser writes the multipart boundary itself.
  const res = await apiFetch('/api/admin/flow-library/import?mode=install', {
    method: 'POST',
    body: fd,
  })
  if (res.status === 409) {
    // Idempotent re-import — surface as typed result so callers can decide.
    const body = await res.json() as { id: string; status: SpecBundleStatus }
    return { id: body.id, status: body.status, reproReport: null }
  }
  return jsonOrThrow<ImportInstallResult>(res)
}

export async function importBundleDraft(file: File): Promise<ImportDraftResult> {
  const fd = new FormData()
  fd.append('file', file)
  const res = await apiFetch('/api/admin/flow-library/import?mode=draft', {
    method: 'POST',
    body: fd,
  })
  return jsonOrThrow<ImportDraftResult>(res)
}

export async function reproCheckBundle(id: string): Promise<ReproReport> {
  const res = await apiFetch(`/api/admin/flow-library/${id}/repro-check`, {
    method: 'POST',
  })
  return jsonOrThrow<ReproReport>(res)
}

export async function deleteBundle(id: string): Promise<void> {
  const res = await apiFetch(`/api/admin/flow-library/${id}`, { method: 'DELETE' })
  if (!res.ok && res.status !== 204) {
    let detail = `${res.status} ${res.statusText}`
    try { const body = await res.json(); if (body?.error) detail = body.error } catch { /* ignore */ }
    throw new Error(detail)
  }
}

// Re-export common types for screen consumers.
export type {
  BundleManifest,
  FlowLibraryDetailDto,
  FlowLibraryItemDto,
  ImportDraftResult,
  ImportInstallResult,
  ReproReport,
  SampleOrgSnapshot,
  TestCaseSnapshot,
  BundleValidationResult,
  SpecBundleStatus,
}
