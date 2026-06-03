import { useEffect, useRef, useState } from 'react'
import { Image as ImageIcon, Save, Trash2, Upload } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Field, Input } from '@/components/ui/form'
import { getBranding, putBranding } from '@/flowcook/api/branding'

const LOGO_MAX = 256 * 1024
const FAVICON_MAX = 64 * 1024

/**
 * White-label branding editor. System name + logo + favicon are written to
 * bpm-svc's shared TenantSettings (via the /bpmsvc dev proxy) and read back
 * by the employee app on its next load.
 */
export function BrandingTab() {
  const [systemName, setSystemName] = useState('')
  const [logo, setLogo] = useState<string | null>(null)
  const [favicon, setFavicon] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  useEffect(() => {
    void (async () => {
      try {
        const b = await getBranding()
        setSystemName(b.systemName ?? '')
        setLogo(b.logoDataUri)
        setFavicon(b.faviconDataUri)
      } catch (e) {
        setError(e instanceof Error ? e.message : 'load failed')
      } finally {
        setLoading(false)
      }
    })()
  }, [])

  const readFile = (file: File, max: number, set: (d: string) => void) => {
    if (file.size > max) {
      setError(`檔案太大（${Math.round(file.size / 1024)}KB，上限 ${Math.round(max / 1024)}KB）`)
      return
    }
    const reader = new FileReader()
    reader.onload = () => { set(reader.result as string); setError(null); setSaved(false) }
    reader.onerror = () => setError('讀取檔案失敗')
    reader.readAsDataURL(file)
  }

  const save = async () => {
    setSaving(true); setError(null); setSaved(false)
    try {
      const b = await putBranding({
        systemName: systemName.trim() || null,
        logoDataUri: logo,
        faviconDataUri: favicon,
      })
      setSystemName(b.systemName ?? '')
      setLogo(b.logoDataUri)
      setFavicon(b.faviconDataUri)
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'save failed')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <div className="px-1 py-8 text-sm text-ink-muted">載入中…</div>

  return (
    <div className="max-w-2xl space-y-5">
      <p className="text-sm text-ink-muted">
        客戶白牌設定：系統名稱、Logo、Favicon。儲存後員工端（bpm）的左上角、登入頁、瀏覽器分頁標題與圖示會跟著套用；員工端重新整理即可看到。
      </p>

      <Card className="space-y-5">
        <Field label="系統名稱 / System Name" hint="顯示在 header 與登入頁；留空則用預設「BPM System」。">
          <Input value={systemName} onChange={e => { setSystemName(e.target.value); setSaved(false) }} placeholder="e.g. ACME 企業流程平台" />
        </Field>

        <ImageField
          label="Logo（PNG / SVG，上限 256KB）"
          dataUri={logo}
          previewClass="max-h-10 max-w-[120px]"
          onPick={f => readFile(f, LOGO_MAX, setLogo)}
          onClear={() => { setLogo(null); setSaved(false) }}
        />

        <ImageField
          label="Favicon（建議 32×32，PNG / ICO / SVG，上限 64KB）"
          dataUri={favicon}
          previewClass="h-8 w-8"
          onPick={f => readFile(f, FAVICON_MAX, setFavicon)}
          onClear={() => { setFavicon(null); setSaved(false) }}
        />
      </Card>

      {error && <p className="text-sm text-danger">{error}</p>}

      <div className="flex items-center gap-3">
        <Button variant="primary" size="md" onClick={save} disabled={saving}>
          <Save className="h-4 w-4" /> {saving ? '儲存中…' : '儲存'}
        </Button>
        {saved && <span className="text-sm text-good">已儲存 ✓ 員工端重新整理即可看到</span>}
      </div>
    </div>
  )
}

function ImageField({ label, dataUri, previewClass, onPick, onClear }: {
  label: string
  dataUri: string | null
  previewClass: string
  onPick: (file: File) => void
  onClear: () => void
}) {
  const ref = useRef<HTMLInputElement>(null)
  return (
    <Field label={label}>
      <div className="flex items-center gap-3">
        <div className="flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded border border-rule bg-slate-50">
          {dataUri ? <img src={dataUri} alt="" className={previewClass} /> : <ImageIcon className="h-5 w-5 text-ink-faint" />}
        </div>
        <input
          ref={ref}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={e => { const f = e.target.files?.[0]; if (f) onPick(f); e.target.value = '' }}
        />
        <Button variant="outline" size="sm" onClick={() => ref.current?.click()}>
          <Upload className="h-3.5 w-3.5" /> 選擇檔案
        </Button>
        {dataUri && (
          <Button variant="ghost" size="sm" onClick={onClear}>
            <Trash2 className="h-3.5 w-3.5" /> 移除
          </Button>
        )}
      </div>
    </Field>
  )
}
