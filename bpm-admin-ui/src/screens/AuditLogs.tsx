import { Activity } from 'lucide-react'

export function AuditLogs() {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Activity className="h-5 w-5 text-ink-muted" />
        <h1 className="text-xl font-bold text-ink">Audit Logs</h1>
      </div>
      <p className="text-sm text-ink-muted">Future: cross-cutting audit search across all capabilities.</p>
    </div>
  )
}
