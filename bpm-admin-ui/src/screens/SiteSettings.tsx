import { useEffect, useState } from 'react'
import { Settings, Loader2, AlertTriangle, Check, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Input, Textarea, Field } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { getSandboxStatus, setSandboxStatus, getSandboxRedirects } from '@/lib/api/sandbox'
import { SandboxAction, SandboxChannel, type SandboxRedirectDto, type SandboxStatusDto } from '@/types/sandbox'

export function SiteSettings() {
  const [status, setStatus] = useState<SandboxStatusDto | null>(null)
  const [redirects, setRedirects] = useState<SandboxRedirectDto[]>([])
  const [emailRecipients, setEmailRecipients] = useState('')
  const [webhookUrl, setWebhookUrl] = useState('')
  const [smsRecipients, setSmsRecipients] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<null | { title: string; description: string; onConfirm: () => void }>(null)
  const [toast, setToast] = useState<string | null>(null)

  const fireToast = (m: string) => { setToast(m); setTimeout(() => setToast(null), 2400) }

  async function refresh() {
    try {
      const [s, r] = await Promise.all([getSandboxStatus(), getSandboxRedirects(30)])
      setStatus(s)
      setRedirects(r)
      setEmailRecipients((s.config?.emailRecipients ?? []).join('\n'))
      setWebhookUrl(s.config?.webhookUrl ?? '')
      setSmsRecipients((s.config?.smsRecipients ?? []).join('\n'))
    } catch (e) {
      setErr((e as Error).message)
    }
  }

  useEffect(() => { void refresh() }, [])

  function buildConfig() {
    return {
      emailRecipients: emailRecipients.split(/\s*[\n,]\s*/).map(s => s.trim()).filter(Boolean),
      webhookUrl: webhookUrl.trim() || null,
      smsRecipients: smsRecipients.split(/\s*[\n,]\s*/).map(s => s.trim()).filter(Boolean),
    }
  }

  async function saveConfigOnly() {
    if (!status) return
    setBusy(true)
    try {
      const updated = await setSandboxStatus(status.enabled, buildConfig())
      setStatus(updated)
      fireToast('Config saved.')
    } catch (e) {
      fireToast(`Save failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  async function toggleSandbox(target: boolean) {
    if (!status) return
    setBusy(true)
    try {
      const updated = await setSandboxStatus(target, buildConfig())
      setStatus(updated)
      await refresh()
      fireToast(target ? 'Sandbox ON. All outbound now redirected.' : 'Sandbox OFF. Live dispatch resumed.')
    } catch (e) {
      fireToast(`Toggle failed: ${(e as Error).message}`)
    } finally { setBusy(false) }
  }

  if (err && !status) {
    return (
      <div className="text-sm text-danger">Failed to load sandbox status: {err}</div>
    )
  }

  return (
    <div className="space-y-4">
      {toast && <div className="fixed bottom-6 right-6 z-50 rounded-lg bg-slate-800 px-4 py-2.5 text-sm text-white shadow-2xl">{toast}</div>}

      <div className="flex items-center gap-2">
        <Settings className="h-5 w-5 text-ink-muted" />
        <h1 className="text-xl font-bold text-ink">Site Settings</h1>
      </div>

      <SectionCard>
        <SectionTitle>
          <span className="inline-flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 text-amber-600" />
            Sandbox Mode
          </span>
        </SectionTitle>
        <div className="space-y-4 p-5">
          {status && (
            <div className={`flex items-center justify-between rounded-md border-2 p-4 ${status.enabled ? 'border-red-300 bg-red-50' : 'border-slate-200 bg-slate-50'}`}>
              <div>
                <div className="flex items-center gap-2 text-sm font-semibold">
                  {status.enabled ? <Check className="h-4 w-4 text-red-600" /> : <X className="h-4 w-4 text-slate-400" />}
                  Sandbox is currently <strong>{status.enabled ? 'ON' : 'OFF'}</strong>
                </div>
                {status.lastToggledAt && (
                  <p className="mt-1 text-xs text-ink-muted">
                    Last toggled {new Date(status.lastToggledAt).toLocaleString()}
                  </p>
                )}
              </div>
              <div className="flex gap-2">
                {status.enabled ? (
                  <Button variant="outline" size="md" onClick={() => setConfirm({
                    title: 'Turn OFF sandbox mode?',
                    description: 'Outbound emails / webhooks / SMS will resume going to real recipients immediately.',
                    onConfirm: () => toggleSandbox(false),
                  })} disabled={busy}>Turn OFF</Button>
                ) : (
                  <Button variant="primary" size="md" onClick={() => setConfirm({
                    title: 'Turn ON sandbox mode?',
                    description: 'This affects ALL outbound communication. Make sure your test recipients are set up.',
                    onConfirm: () => toggleSandbox(true),
                  })} disabled={busy}>Turn ON</Button>
                )}
              </div>
            </div>
          )}

          <div className="space-y-3 border-t border-rule pt-4">
            <Field label="Email test recipients" hint="One per line, or comma-separated. All outbound emails go here when sandbox is ON.">
              <Textarea rows={3} value={emailRecipients} onChange={e => setEmailRecipients(e.target.value)} placeholder="uat@acme.com" />
            </Field>
            <Field label="Webhook test URL" hint="Outbound webhooks redirect here. Use webhook.site or similar.">
              <Input value={webhookUrl} onChange={e => setWebhookUrl(e.target.value)} placeholder="https://webhook.site/<uuid>" />
            </Field>
            <Field label="SMS test recipients" hint="One per line. Future SMS dispatches go here.">
              <Textarea rows={2} value={smsRecipients} onChange={e => setSmsRecipients(e.target.value)} placeholder="+886900000000" />
            </Field>
            <div className="flex justify-end">
              <Button variant="primary" size="sm" onClick={saveConfigOnly} disabled={busy}>
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Save config
              </Button>
            </div>
          </div>
        </div>
      </SectionCard>

      <SectionCard>
        <SectionTitle>Recent redirects (last 30 days)</SectionTitle>
        {redirects.length === 0 ? (
          <div className="px-5 py-6 text-sm text-ink-muted">No redirects yet. They will appear here when sandbox attribution is exercised by the notification engine.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="border-b border-rule bg-slate-50 text-[11px] uppercase tracking-wider text-ink-muted">
                <tr>
                  <th className="px-4 py-2 text-left">When</th>
                  <th className="px-4 py-2 text-left">Channel</th>
                  <th className="px-4 py-2 text-left">Action</th>
                  <th className="px-4 py-2 text-left">Original → Redirected</th>
                  <th className="px-4 py-2 text-left">Sample</th>
                </tr>
              </thead>
              <tbody>
                {redirects.map(r => (
                  <tr key={r.id} className="border-b border-rule last:border-b-0">
                    <td className="px-4 py-2 font-mono text-xs">{new Date(r.dispatchedAt).toLocaleString()}</td>
                    <td className="px-4 py-2">{channelLabel(r.channel)}</td>
                    <td className={`px-4 py-2 ${r.action === SandboxAction.Dropped ? 'text-danger' : 'text-ink'}`}>{r.action === SandboxAction.Dropped ? 'Dropped' : 'Redirected'}</td>
                    <td className="px-4 py-2 text-xs">{r.originalTargets.join(', ')} → {r.redirectedTargets.length === 0 ? '—' : r.redirectedTargets.join(', ')}</td>
                    <td className="px-4 py-2 text-xs text-ink-muted">{r.sampleSubject}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SectionCard>

      <ConfirmDialog
        open={!!confirm}
        title={confirm?.title ?? ''}
        description={confirm?.description}
        tone="danger"
        confirmText="Confirm"
        onCancel={() => setConfirm(null)}
        onConfirm={() => { confirm?.onConfirm(); setConfirm(null) }}
      />
    </div>
  )
}

function channelLabel(c: number): string {
  switch (c) {
    case SandboxChannel.Email: return 'Email'
    case SandboxChannel.Webhook: return 'Webhook'
    case SandboxChannel.Sms: return 'SMS'
  }
  return '?'
}
