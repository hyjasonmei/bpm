import { useEffect, useRef, useState } from 'react'
import { HelpCircle, Bug, BookOpen, MessageSquare, X, Loader2, Check } from 'lucide-react'
import { cn } from '@/lib/cn'
import { Button } from '@/components/ui/button'
import { Input, Textarea, Field, Select } from '@/components/ui/form'

type IssueKind = 'bug' | 'feature' | 'question'

export function HelpReportMenu() {
  const [open, setOpen] = useState(false)
  const [reportOpen, setReportOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onDocClick = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onEsc = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onEsc)
    return () => {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onEsc)
    }
  }, [open])

  return (
    <div ref={ref} className="relative">
      <button
        title="Help"
        onClick={() => setOpen(o => !o)}
        className={cn(
          'flex h-8 w-8 items-center justify-center rounded text-white/70 transition-colors hover:bg-white/10 hover:text-white',
          open && 'bg-white/10 text-white',
        )}
      >
        <HelpCircle className="h-4 w-4" />
      </button>
      {open && (
        <div className="absolute right-0 top-[calc(100%+6px)] z-30 w-64 origin-top-right rounded-lg border border-rule bg-card text-ink shadow-2xl">
          <div className="border-b border-rule px-4 py-2">
            <p className="text-sm font-semibold">Help</p>
          </div>
          <button
            onClick={() => { setOpen(false); window.alert('User Guide — placeholder') }}
            className="flex w-full items-center gap-2.5 px-4 py-2.5 text-left text-sm text-ink-muted hover:bg-slate-50 hover:text-ink"
          >
            <BookOpen className="h-4 w-4" />
            User Guide
          </button>
          <button
            onClick={() => { setOpen(false); setReportOpen(true) }}
            className="flex w-full items-center gap-2.5 border-t border-rule px-4 py-2.5 text-left text-sm text-ink-muted hover:bg-amber-50 hover:text-amber-800"
          >
            <Bug className="h-4 w-4" />
            Report an issue
          </button>
          <a
            href="mailto:support@bpm.local"
            className="flex w-full items-center gap-2.5 border-t border-rule px-4 py-2.5 text-left text-sm text-ink-muted hover:bg-slate-50 hover:text-ink"
          >
            <MessageSquare className="h-4 w-4" />
            Contact support
          </a>
        </div>
      )}
      <ReportIssueModal open={reportOpen} onClose={() => setReportOpen(false)} />
    </div>
  )
}

function ReportIssueModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [kind, setKind] = useState<IssueKind>('bug')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [contact, setContact] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  useEffect(() => {
    if (!open) {
      setTitle(''); setDescription(''); setContact(''); setKind('bug'); setBusy(false); setDone(false)
    }
  }, [open])

  if (!open) return null

  async function fakeSubmit() {
    if (!title.trim() || !description.trim()) return
    setBusy(true)
    // Pretend to ship to engineers — just a delay so the spinner shows.
    await new Promise(r => setTimeout(r, 700))
    // In real implementation this would POST to /api/support/issues with
    // current screen kind, JWT identity, persona, browser/UA snapshot, etc.
    console.info('[issue-report] would dispatch:', { kind, title, description, contact })
    setBusy(false)
    setDone(true)
    setTimeout(() => onClose(), 1400)
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4">
      <div className="w-full max-w-lg rounded-lg border border-rule bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-rule px-4 py-3">
          <div className="flex items-center gap-2">
            <Bug className="h-4 w-4 text-amber-600" />
            <h2 className="text-sm font-semibold text-ink">Report an issue</h2>
          </div>
          <button onClick={onClose} className="text-ink-muted hover:text-ink"><X className="h-4 w-4" /></button>
        </div>
        {done ? (
          <div className="flex flex-col items-center gap-2 p-8">
            <div className="flex h-10 w-10 items-center justify-center rounded-full bg-green-100 text-green-700">
              <Check className="h-5 w-5" />
            </div>
            <p className="text-sm font-semibold text-ink">Sent to engineering</p>
            <p className="text-xs text-ink-muted">Thanks — we'll get back to you if we need more detail.</p>
          </div>
        ) : (
          <div className="space-y-3 p-4">
            <Field label="Type">
              <Select value={kind} onChange={e => setKind(e.target.value as IssueKind)}>
                <option value="bug">Bug — something's broken</option>
                <option value="feature">Feature request — I want it to do X</option>
                <option value="question">Question — how do I…</option>
              </Select>
            </Field>
            <Field label="Title" required>
              <Input value={title} onChange={e => setTitle(e.target.value)} placeholder="One-line summary" />
            </Field>
            <Field label="Description" required hint="What were you doing? What happened? What did you expect?">
              <Textarea rows={5} value={description} onChange={e => setDescription(e.target.value)} placeholder="Include steps to reproduce if it's a bug." />
            </Field>
            <Field label="Reach me at (optional)" hint="Slack handle, email, or extension. Leave blank if you don't need a follow-up.">
              <Input value={contact} onChange={e => setContact(e.target.value)} placeholder="e.g. @wilson on Slack" />
            </Field>
            <p className="text-[10px] text-ink-faint">
              We'll attach: current page, your account, browser version. No screenshots — paste in the description if relevant.
            </p>
            <div className="flex justify-end gap-2 border-t border-rule pt-3">
              <Button variant="outline" size="sm" onClick={onClose} disabled={busy}>Cancel</Button>
              <Button variant="primary" size="sm" onClick={fakeSubmit} disabled={busy || !title.trim() || !description.trim()}>
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Send to engineering
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
