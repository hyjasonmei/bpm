import { useEffect, useState } from 'react'

import { apiFetch } from '@/lib/apiFetch'

/** White-label branding served by bpm-svc (`GET /api/branding`, anonymous). */
export interface Branding {
  systemName: string | null
  logoDataUri: string | null
  faviconDataUri: string | null
}

const DEFAULT: Branding = { systemName: null, logoDataUri: null, faviconDataUri: null }

// Fetched once per page load and shared across the header / login / document.
let cache: Branding | null = null
let inflight: Promise<Branding> | null = null

function fetchBranding(): Promise<Branding> {
  if (cache) return Promise.resolve(cache)
  if (!inflight) {
    inflight = apiFetch('/api/branding')
      .then(r => (r.ok ? (r.json() as Promise<Branding>) : DEFAULT))
      .then(b => { cache = b ?? DEFAULT; return cache })
      .catch(() => DEFAULT)
  }
  return inflight
}

/** Subscribe to branding; returns DEFAULT until the fetch resolves. */
export function useBranding(): Branding {
  const [branding, setBranding] = useState<Branding>(cache ?? DEFAULT)
  useEffect(() => {
    let on = true
    void fetchBranding().then(b => { if (on) setBranding(b) })
    return () => { on = false }
  }, [])
  return branding
}

/** Apply branding to the document chrome (tab title + favicon). */
export function applyBrandingToDocument(b: Branding): void {
  if (b.systemName) document.title = b.systemName
  if (b.faviconDataUri) {
    let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]')
    if (!link) {
      link = document.createElement('link')
      link.rel = 'icon'
      document.head.appendChild(link)
    }
    link.href = b.faviconDataUri
  }
}
