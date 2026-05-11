import { CapturedListWithModal } from './MailTab'
import { SandboxChannel, type CapturedMessageDetailDto } from '@/types/sandbox'

export function WebhooksTab() {
  return (
    <CapturedListWithModal
      channel={SandboxChannel.Webhook}
      emptyLabel="No captured webhook deliveries yet."
      columns={[
        { header: 'Event type', render: r => <span className="font-mono text-[11px]">{r.eventType ?? '—'}</span> },
        { header: 'Subject',    render: r => r.subject ?? <em className="text-ink-faint">(none)</em> },
      ]}
      DetailRender={WebhookDetail}
    />
  )
}

function WebhookDetail({ d }: { d: CapturedMessageDetailDto }) {
  return (
    <div className="space-y-3">
      <div className="inline-flex items-center gap-1 rounded bg-emerald-100 px-2 py-0.5 text-[10.5px] font-bold uppercase tracking-wider text-emerald-800">
        Fake 200 OK
      </div>
      <KV label="URL" value={d.url ?? '—'} mono />
      <KV label="Event type" value={d.eventType ?? '—'} mono />
      <KV label="Originating subscription" value={d.originatingWebhookSubscriptionId ?? '—'} mono />
      <KV label="Process instance" value={d.processInstanceId ?? '—'} mono />
      <KV label="Intended recipients (URL)" value={d.intendedRecipients.join(', ') || '—'} mono />
      {d.headersJson && (
        <div>
          <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint mb-1">Headers</p>
          <pre className="max-h-40 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px]">{prettyJson(d.headersJson)}</pre>
        </div>
      )}
      {d.payloadJson && (
        <div>
          <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint mb-1">Payload</p>
          <pre className="max-h-72 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px]">{prettyJson(d.payloadJson)}</pre>
        </div>
      )}
    </div>
  )
}

function KV({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint">{label}</p>
      <p className={mono ? 'font-mono text-[11.5px] text-ink break-all' : 'text-sm text-ink'}>{value}</p>
    </div>
  )
}

function prettyJson(raw: string): string {
  try { return JSON.stringify(JSON.parse(raw), null, 2) }
  catch { return raw }
}
