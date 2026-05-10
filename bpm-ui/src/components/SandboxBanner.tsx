import { useEffect, useState } from 'react'
import { TestTube } from 'lucide-react'
import { apiFetch } from '@/lib/apiFetch'

interface SandboxStatus { enabled: boolean }

export function SandboxBanner() {
  const [enabled, setEnabled] = useState(false)
  useEffect(() => {
    let cancelled = false
    apiFetch('/api/sandbox/status')
      .then(r => r.ok ? r.json() : null)
      .then((s: SandboxStatus | null) => { if (!cancelled && s) setEnabled(s.enabled) })
      .catch(() => { /* swallow */ })
    return () => { cancelled = true }
  }, [])

  if (!enabled) return null
  return (
    <div className="flex items-center justify-center gap-2 bg-amber-500 px-4 py-1.5 text-xs font-semibold text-white shadow">
      <TestTube className="h-3.5 w-3.5" />
      🧪 SANDBOX MODE ACTIVE — outbound emails / webhooks are being redirected to test recipients
    </div>
  )
}
