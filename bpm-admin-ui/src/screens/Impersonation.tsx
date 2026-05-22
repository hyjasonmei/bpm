import { Eye } from 'lucide-react'

export function Impersonation() {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Eye className="h-5 w-5 text-ink-muted" />
        <h1 className="text-xl font-bold text-ink">Impersonation</h1>
      </div>
      <p className="text-sm text-ink-muted">Active sessions and history — populated by add-user-impersonation.</p>
    </div>
  )
}
