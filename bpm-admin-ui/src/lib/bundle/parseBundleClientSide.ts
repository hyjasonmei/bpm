/**
 * Pure-browser bundle inspector. Used by the Import drag-drop flow to
 * preview the manifest + file list *before* the user commits to install
 * vs draft — no round-trip to the server.
 *
 * Server-side parsing (BundleParser.cs in bpm-svc) re-verifies SHA-256
 * per file; this client-side pass skips that cost because the user hasn't
 * decided to upload yet. The server enforces integrity once they do.
 */

import JSZip from 'jszip'
import type { BundleManifest } from '@/types/flowLibrary'

export interface ClientSideBundlePreview {
  manifest: BundleManifest
  fileList: { path: string; sizeBytes: number }[]
}

interface JSZipInternalEntry {
  dir: boolean
  // jszip stores uncompressed size on the internal `_data` blob; the public
  // API doesn't expose it. Optional cast — fall back to 0 if shape changes.
  _data?: { uncompressedSize?: number }
}

export async function parseBundleClientSide(file: File): Promise<ClientSideBundlePreview> {
  const z = await JSZip.loadAsync(file)
  const manifestEntry = z.file('manifest.json')
  if (!manifestEntry) {
    throw new Error('bundle is missing manifest.json (not a Flow Library bundle)')
  }
  const manifestText = await manifestEntry.async('string')

  let manifest: BundleManifest
  try {
    manifest = JSON.parse(manifestText) as BundleManifest
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e)
    throw new Error(`manifest.json is not valid JSON: ${msg}`)
  }

  const fileList: { path: string; sizeBytes: number }[] = []
  for (const [path, entry] of Object.entries(z.files)) {
    const e = entry as unknown as JSZipInternalEntry
    if (e.dir) continue
    fileList.push({
      path,
      sizeBytes: e._data?.uncompressedSize ?? 0,
    })
  }
  // Stable order: manifest first, then alphabetical — matches the way
  // the detail screen later renders the file list.
  fileList.sort((a, b) => {
    if (a.path === 'manifest.json') return -1
    if (b.path === 'manifest.json') return 1
    return a.path.localeCompare(b.path)
  })

  return { manifest, fileList }
}
