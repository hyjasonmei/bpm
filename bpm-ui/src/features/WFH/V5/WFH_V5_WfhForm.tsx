import { useEffect, useMemo, useState } from 'react'
import { CalendarIcon } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { FilePicker, type FilePickerValue } from '@/components/ui/FilePicker'
import { ActionFooter } from '@/components/ui/action-footer/ActionFooter'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { ownerLabel } from '@/lib/workflow'
import { consecutiveDays, DAYS_GATE_THRESHOLD, needsSenior } from './WFH_V5_shared'
import type { WFH_V5_CaseResponse } from './WFH_V5_types'

/** Today's date as a yyyy-mm-dd string for the apply_date default. */
function todayIso(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/**
 * WFH V5 — submitter form (task_apply). Posts to <c>POST /api/wfh/v5</c>;
 * with <c>?resubmit=&lt;caseId&gt;</c> pre-fills and POSTs to
 * <c>/{caseId}/resubmit</c>. Approval actions live on the case-detail page
 * (`WFH_V5_CaseDetail`) — the form component is create / resubmit only.
 *
 * V5 raises the senior-approval gateway to ≥ 90 consecutive days (V4: ≥ 60).
 *
 * The `applicant` field is fixed to the logged-in user (spec
 * permissions.submitter = self), shown read-only rather than as a picker.
 */
export function WFH_V5_WfhForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [applyDate, setApplyDate] = useState(todayIso())
  const [start, setStart] = useState('')
  const [end, setEnd] = useState('')
  const [reason, setReason] = useState('')
  const [attachment, setAttachment] = useState<FilePickerValue | null>(null)
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/wfh/v5/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const body = (await res.json()) as WFH_V5_CaseResponse
        if (cancelled) return
        setApplyDate(body.applyDate?.slice(0, 10) || todayIso())
        setStart(body.startDate?.slice(0, 10) ?? '')
        setEnd(body.endDate?.slice(0, 10) ?? '')
        setReason(body.reason)
        setAttachment(body.attachmentFileId ? { id: body.attachmentFileId, fileName: '已上傳附件', contentType: '', sizeBytes: 0 } : null)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  const days = useMemo(() => consecutiveDays(start, end), [start, end])
  const validRange = !start || !end ? true : new Date(start).getTime() <= new Date(end).getTime()
  const senior = needsSenior(days)
  const valid = !!applyDate && !!start && !!end && validRange && days > 0 && !!reason.trim()

  if (mode !== 'create') {
    return (
      <FormShell code="WFH" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard>
          <div className="px-5 py-8 text-center text-sm text-ink-muted">
            開啟此申請的詳細頁面以核准 / 退件。請從首頁的「Pending My Approval」進入。
          </div>
        </SectionCard>
      </FormShell>
    )
  }

  function attemptSubmit() {
    if (!validRange || !days) { setError('請選擇有效的居家辦公起訖日期。'); return }
    if (!reason.trim()) { setError('請填寫申請原因。'); return }
    setError(null)
    setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false)
    setPending(true)
    setError(null)
    const payload = {
      applyDate,
      dateRange: { start, end },
      reason: reason.trim(),
      attachmentFileId: attachment?.id ?? null,
    }
    const url = isResubmit ? `/api/wfh/v5/${resubmitCaseId}/resubmit` : '/api/wfh/v5'
    try {
      const res = await apiFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })
      if (!res.ok) throw new Error((await res.text()) || `HTTP ${res.status}`)
      const body = (await res.json()) as WFH_V5_CaseResponse
      onSubmitted?.()
      navigate(`/cases/wfh/${body.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="WFH" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>申請資訊 / Application</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            居家辦公申請；申請人固定為您本人。連續日期達 <span className="font-medium">{DAYS_GATE_THRESHOLD}</span> 天（含）以上會自動加簽上級主管。
            {isResubmit && (
              <span className="mt-1 block text-amber-900">
                此案件先前被退回，請依照退件意見修正後重新送出（將進入新的審核回合）。
              </span>
            )}
          </InfoBanner>
        </div>
        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-12 gap-4 px-5 py-4">
            <Field label="申請人 / Applicant" className="md:col-span-12 sm:md:col-span-6">
              <div className="flex h-8 items-center rounded-md border border-rule bg-slate-50 px-2.5 text-sm text-ink-muted">
                {ownerLabel((persona as PersonaCode) ?? null)}（您本人）
              </div>
            </Field>
            <Field label="申請日期 / Application Date" required className="md:col-span-12 sm:md:col-span-6">
              <div className="relative">
                <Input type="date" value={applyDate} onChange={e => setApplyDate(e.target.value)} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>

            <Field label="居家辦公起 / Start date" required className="md:col-span-6">
              <div className="relative">
                <Input type="date" value={start} onChange={e => setStart(e.target.value)} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
            <Field label="居家辦公迄 / End date" required className="md:col-span-6">
              <div className="relative">
                <Input type="date" value={end} onChange={e => setEnd(e.target.value)} disabled={pending} />
                <CalendarIcon className="pointer-events-none absolute right-2 top-2 h-4 w-4 text-ink-faint" />
              </div>
            </Field>
          </div>
        )}

        {/* Computed consecutive-days echo — mirrors the gateway the backend evaluates. */}
        {!loading && (
          <div className="flex flex-wrap items-center justify-between gap-x-6 gap-y-1 border-t border-rule bg-slate-50 px-5 py-3">
            <span className="text-sm font-medium text-ink-muted">
              連續天數 / Consecutive days <span className="text-ink-faint">（含頭尾，自動計算）</span>
            </span>
            <span className="font-mono text-base">
              {!validRange ? <span className="font-semibold text-danger">起訖反向 / Invalid range</span>
                : days === 0 ? <span className="text-ink-faint">—</span>
                : <span className="text-ink"><span className="font-bold tabular">{days}</span> {days === 1 ? 'day' : 'days'}{senior && <span className="ml-2 text-amber-700">· 需上級加簽</span>}</span>}
            </span>
          </div>
        )}
      </SectionCard>

      <SectionCard>
        <SectionTitle>原因與附件 / Reason & Attachment</SectionTitle>
        <div className="grid grid-cols-1 md:grid-cols-12 gap-4 px-5 py-4">
          <Field label="申請原因 / Reason" required hint="中英文皆可" className="md:col-span-7">
            <Textarea
              rows={4}
              value={reason}
              onChange={e => setReason(e.target.value)}
              placeholder="e.g. 家中需照護 / Caregiving at home"
              disabled={pending}
            />
          </Field>
          <Field label="附件 / Attachment (選填)" hint="可附證明文件 PDF / JPG / PNG" className="md:col-span-5">
            <FilePicker
              value={attachment}
              onChange={setAttachment}
              disabled={pending}
              accept=".pdf,.png,.jpg,.jpeg"
              placeholder="PDF/JPG/PNG（選填）"
            />
          </Field>
        </div>
      </SectionCard>

      <ActionFooter
        hint={error ? <span className="text-danger">{error}</span> : <span>{isResubmit ? '送出後將重新通知您的主管。' : '送出後將通知您的主管。'}</span>}
        actions={[
          { id: 'cancel', label: '取消', variant: 'ghost', disabled: pending, onClick: () => navigate('/') },
          { id: 'submit', label: isResubmit ? '重新送出' : '送出申請', variant: 'primary', pending, disabled: !valid, onClick: attemptSubmit },
        ]}
      />

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit WFH request?' : 'Submit WFH request?'}
        titleZh={isResubmit ? '重新送出居家辦公申請？' : '送出居家辦公申請？'}
        description={`${days} ${days === 1 ? 'day' : 'days'} 居家辦公${senior ? `（≥${DAYS_GATE_THRESHOLD} 天，需主管 + 上級主管核准）` : ''} 將送交主管核准。`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
