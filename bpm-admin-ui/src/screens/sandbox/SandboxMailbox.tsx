/**
 * Sandbox Mailbox admin screen — PR-J5 §9.
 *
 * Tabbed shell (Mail / Webhooks / SMS / Clock) shown to admins so they can
 * inspect the captures the sandbox gate writes instead of the original
 * outbound traffic. Tab strip mirrors the BundleDetail (PR-I6) sidebar
 * convention so admins don't relearn nav.
 */
import { useState } from 'react'
import { Mail, Webhook, MessageSquare, Clock } from 'lucide-react'
import { MailTab } from './MailTab'
import { WebhooksTab } from './WebhooksTab'
import { SmsTab } from './SmsTab'
import { ClockTab } from './ClockTab'

type TabKey = 'mail' | 'webhooks' | 'sms' | 'clock'

const TABS: { key: TabKey; label: string; icon: React.ReactNode }[] = [
  { key: 'mail',     label: 'Mail',     icon: <Mail className="h-3.5 w-3.5" /> },
  { key: 'webhooks', label: 'Webhooks', icon: <Webhook className="h-3.5 w-3.5" /> },
  { key: 'sms',      label: 'SMS',      icon: <MessageSquare className="h-3.5 w-3.5" /> },
  { key: 'clock',    label: 'Clock',    icon: <Clock className="h-3.5 w-3.5" /> },
]

export function SandboxMailbox() {
  const [tab, setTab] = useState<TabKey>('mail')

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-2xl font-bold text-ink">Sandbox Mailbox</h1>
        <p className="text-sm text-ink-muted">
          Outbound emails, webhooks, and SMS the sandbox captured instead of
          dispatching, plus the time-travel clock control.
        </p>
      </header>

      <div className="flex min-h-[60vh] gap-4">
        <aside className="w-44 shrink-0 rounded border border-rule bg-card p-1.5">
          {TABS.map(t => (
            <button
              key={t.key}
              onClick={() => setTab(t.key)}
              className={[
                'flex w-full items-center gap-2 rounded px-2.5 py-1.5 text-left text-xs font-medium transition-colors',
                tab === t.key
                  ? 'bg-slate-100 text-ink shadow-sm'
                  : 'text-ink-muted hover:bg-slate-50 hover:text-ink',
              ].join(' ')}
            >
              {t.icon} {t.label}
            </button>
          ))}
        </aside>
        <main className="min-w-0 flex-1 rounded border border-rule bg-white p-4">
          {tab === 'mail'     && <MailTab />}
          {tab === 'webhooks' && <WebhooksTab />}
          {tab === 'sms'      && <SmsTab />}
          {tab === 'clock'    && <ClockTab />}
        </main>
      </div>
    </div>
  )
}
