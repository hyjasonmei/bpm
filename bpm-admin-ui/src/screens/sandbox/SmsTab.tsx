import { CapturedListWithModal } from './MailTab'
import { SandboxChannel, type CapturedMessageDetailDto } from '@/types/sandbox'

export function SmsTab() {
  return (
    <CapturedListWithModal
      channel={SandboxChannel.Sms}
      emptyLabel="No captured SMS messages yet."
      columns={[
        { header: 'Body', render: r => <span className="truncate">{r.subject ?? r.eventType ?? '—'}</span> },
      ]}
      DetailRender={SmsDetail}
    />
  )
}

function SmsDetail({ d }: { d: CapturedMessageDetailDto }) {
  return (
    <div className="space-y-3">
      <KV label="Intended recipients" value={d.intendedRecipients.join(', ') || '—'} mono />
      <KV label="Process instance" value={d.processInstanceId ?? '—'} mono />
      <div>
        <p className="text-[10.5px] font-semibold uppercase tracking-wider text-ink-faint mb-1">Body</p>
        <pre className="max-h-60 overflow-auto rounded border border-rule bg-slate-50 p-2 text-[11px] whitespace-pre-wrap">{d.body ?? d.bodyText ?? '—'}</pre>
      </div>
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
