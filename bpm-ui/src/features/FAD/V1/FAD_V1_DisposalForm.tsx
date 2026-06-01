import { useEffect, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { SectionCard, SectionTitle } from '@/components/ui/card'
import { Field, InfoBanner, Input, Select, Textarea } from '@/components/ui/form'
import { ConfirmDialog } from '@/components/ui/ConfirmDialog'
import { FilePicker, type FilePickerValue } from '@/components/ui/FilePicker'
import { FormShell } from '@/screens/forms/FormShell'
import type { FormComponentProps } from '@/features/registry'
import type { PersonaCode } from '@/lib/role'
import { apiFetch } from '@/lib/apiFetch'
import { REASON_OPTIONS } from './FAD_V1_shared'
import type { FAD_V1_CaseResponse } from './FAD_V1_types'

/** FAD V1 — submitter form (userTask `dr`): disposal request incl. optional photo upload. */
export function FAD_V1_DisposalForm({ persona, mode = 'create', onSubmitted }: FormComponentProps) {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const resubmitCaseId = params.get('resubmit')
  const isResubmit = !!resubmitCaseId

  const [disposalReason, setDisposalReason] = useState(REASON_OPTIONS[0])
  const [assetId, setAssetId] = useState('')
  const [assetName, setAssetName] = useState('')
  const [description, setDescription] = useState('')
  const [photo, setPhoto] = useState<FilePickerValue | null>(null)
  const [loading, setLoading] = useState(isResubmit)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    if (!isResubmit) return
    let cancelled = false
    void (async () => {
      try {
        const res = await apiFetch(`/api/fad/v1/${resubmitCaseId}`)
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        const b = (await res.json()) as FAD_V1_CaseResponse
        if (cancelled) return
        setDisposalReason(b.disposalReason); setAssetId(b.assetId); setAssetName(b.assetName)
        setDescription(b.description ?? '')
        setPhoto(b.photoFileId ? { id: b.photoFileId, fileName: '已上傳檔案', contentType: '', sizeBytes: 0 } : null)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => { cancelled = true }
  }, [isResubmit, resubmitCaseId])

  if (mode !== 'create') {
    return (
      <FormShell code="FAD" activeStep={0} persona={persona as PersonaCode} mode="task">
        <SectionCard><div className="px-5 py-8 text-center text-sm text-ink-muted">請從首頁「Pending My Approval」進入以判別 / 確認。</div></SectionCard>
      </FormShell>
    )
  }

  const valid = !!disposalReason && !!assetId.trim() && !!assetName.trim()

  function attemptSubmit() {
    if (!valid) { setError('請填寫報廢原因 / 資產編號 / 資產名稱。'); return }
    setError(null); setConfirmOpen(true)
  }

  async function doSubmit() {
    setConfirmOpen(false); setPending(true); setError(null)
    const payload = {
      disposalReason, assetId: assetId.trim(), assetName: assetName.trim(),
      description: description.trim() || null, photoFileId: photo?.id ?? null,
    }
    const url = isResubmit ? `/api/fad/v1/${resubmitCaseId}/resubmit` : '/api/fad/v1'
    try {
      const res = await apiFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
      if (!res.ok) throw new Error(await res.text() || `HTTP ${res.status}`)
      const b = (await res.json()) as FAD_V1_CaseResponse
      onSubmitted?.()
      navigate(`/cases/fad/${b.id}`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setPending(false)
    }
  }

  return (
    <FormShell code="FAD" activeStep={0} persona={persona as PersonaCode} mode="create">
      <SectionCard>
        <SectionTitle>處份申請 / Disposal Request</SectionTitle>
        <div className="border-b border-rule px-5 py-3">
          <InfoBanner>
            填寫報廢原因與資產資訊；可附照片。
            {isResubmit && <span className="mt-1 block text-amber-900">此案件先前被退回，請修正後重新送出。</span>}
          </InfoBanner>
        </div>
        {loading ? (
          <div className="px-5 py-10 text-center text-sm text-ink-muted">載入中…</div>
        ) : (
          <div className="grid grid-cols-12 gap-3 px-5 py-4">
            <Field label="報廢原因 / Disposal Reason" required className="col-span-6">
              <Select value={disposalReason} onChange={e => setDisposalReason(e.target.value)} disabled={pending}>
                {REASON_OPTIONS.map(o => <option key={o} value={o}>{o}</option>)}
              </Select>
            </Field>
            <Field label="資產編號 / Asset ID" required className="col-span-6">
              <Input value={assetId} onChange={e => setAssetId(e.target.value)} disabled={pending} placeholder="ASSET-001" />
            </Field>
            <Field label="資產名稱 / Asset Name" required className="col-span-6">
              <Input value={assetName} onChange={e => setAssetName(e.target.value)} disabled={pending} placeholder="Old Laptop" />
            </Field>
            <Field label="照片 / Photo" className="col-span-6">
              <FilePicker value={photo} onChange={setPhoto} disabled={pending} accept=".pdf,.png,.jpg,.jpeg" placeholder="PDF / 圖檔" />
            </Field>
            <Field label="說明 / Description" className="col-span-12">
              <Textarea rows={3} value={description} onChange={e => setDescription(e.target.value)} disabled={pending} />
            </Field>
          </div>
        )}
      </SectionCard>

      <SectionCard>
        <div className="flex items-center justify-between gap-3 px-5 py-3">
          <div className="text-sm text-ink-muted">
            {error ? <span className="text-danger">{error}</span> : <span>送出後將通知您的主管判別。</span>}
          </div>
          <div className="flex items-center gap-2">
            <Button variant="ghost" onClick={() => navigate('/')} disabled={pending}>取消</Button>
            <Button variant="primary" onClick={attemptSubmit} disabled={pending || !valid}>
              {pending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {isResubmit ? '重新送出' : '送出申請'}
            </Button>
          </div>
        </div>
      </SectionCard>

      <ConfirmDialog
        open={confirmOpen}
        title={isResubmit ? 'Resubmit disposal?' : 'Submit disposal?'}
        titleZh={isResubmit ? '重新送出資產處份？' : '送出資產處份？'}
        description={`${assetName} will be sent for IT judgment.`}
        tone="default"
        confirmText={isResubmit ? '確認重新送出' : '確認送出'}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={doSubmit}
      />
    </FormShell>
  )
}
