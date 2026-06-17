import { useEffect, useState } from 'react'
import { CheckCircle2, Pencil, Plus, Save, Server, X, XCircle } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Checkbox, Field, InfoBanner, Input } from '@/components/ui/form'
import { ApiError } from '@/flowcook/api'
import {
  listDeployConfig,
  upsertDeployConfig,
  type DeployEnvConfig,
} from '@/flowcook/api/deployConfig'

const EMPTY: DeployEnvConfig = {
  envName: '',
  resourceGroup: '',
  bpmSvcApp: '',
  adminSvcApp: '',
  bpmUiSwa: '',
  adminUiSwa: '',
  enabled: true,
}

/**
 * Per-environment deploy config editor (Publish→Deploy pipeline, Task 9).
 *
 * Lists the configured envs and lets the operator add / edit one env's Azure
 * resource NAMES (resource group + the two App Services + the two Static Web
 * Apps). NO secrets are entered here — the deploy worker uses its own `az`
 * login and pulls SWA deployment tokens at deploy time.
 */
export function DeployTab() {
  const [envs, setEnvs] = useState<DeployEnvConfig[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Form state. `draft` non-null = the form is open (add or edit).
  const [draft, setDraft] = useState<DeployEnvConfig | null>(null)
  const [isNew, setIsNew] = useState(false)
  const [saving, setSaving] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [savedEnv, setSavedEnv] = useState<string | null>(null)

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setEnvs(await listDeployConfig())
    } catch (e) {
      setError(errMsg(e))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void load() }, [])

  function openAdd() {
    setDraft({ ...EMPTY })
    setIsNew(true)
    setFormError(null)
  }

  function openEdit(env: DeployEnvConfig) {
    setDraft({ ...env })
    setIsNew(false)
    setFormError(null)
  }

  function closeForm() {
    setDraft(null)
    setFormError(null)
  }

  function setField<K extends keyof DeployEnvConfig>(key: K, value: DeployEnvConfig[K]) {
    setDraft((d) => (d ? { ...d, [key]: value } : d))
  }

  async function save() {
    if (!draft) return
    if (!draft.envName.trim()) { setFormError('環境名稱（Env name）為必填'); return }
    setSaving(true)
    setFormError(null)
    try {
      const payload: DeployEnvConfig = {
        ...draft,
        envName: draft.envName.trim(),
        resourceGroup: draft.resourceGroup.trim(),
        bpmSvcApp: draft.bpmSvcApp.trim(),
        adminSvcApp: draft.adminSvcApp.trim(),
        bpmUiSwa: draft.bpmUiSwa.trim(),
        adminUiSwa: draft.adminUiSwa.trim(),
      }
      const saved = await upsertDeployConfig(payload)
      setSavedEnv(saved.envName)
      closeForm()
      await load()
    } catch (e) {
      setFormError(errMsg(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="max-w-3xl space-y-5">
      <p className="text-sm text-ink-muted">
        每個環境（env）部署目標的 Azure 資源設定，供 Publish→Deploy 部署 worker 使用。
      </p>

      <InfoBanner>
        這裡填的都是 <strong>Azure 資源「名稱」</strong>（resource group、App Service、Static Web
        App），<strong>不是機密</strong>。這裡沒有、也不要加任何 token / 憑證欄位 — 部署 worker
        會用自己的 <code className="font-mono">az</code> 登入，並在部署當下自行抓取 SWA
        的 deployment token。
      </InfoBanner>

      {error && <p className="text-sm text-danger">載入失敗：{error}</p>}
      {savedEnv && (
        <p className="rounded border border-good/30 bg-good/5 px-3 py-2 text-sm text-ink">
          ✓ 環境「{savedEnv}」的部署設定已儲存。
        </p>
      )}

      {/* Existing envs */}
      {loading ? (
        <div className="px-1 py-8 text-sm text-ink-muted">載入中…</div>
      ) : envs.length === 0 ? (
        <Card className="text-sm text-ink-muted">尚未設定任何環境。按下方「新增環境」開始。</Card>
      ) : (
        <div className="space-y-3">
          {envs.map((env) => (
            <Card key={env.envName} className="space-y-3">
              <div className="flex items-start justify-between gap-3">
                <div className="flex items-center gap-2">
                  <Server className="h-4 w-4 text-ink-muted" />
                  <span className="text-sm font-semibold text-ink">{env.envName}</span>
                  {env.enabled ? (
                    <span className="inline-flex items-center gap-1 rounded-full bg-good/10 px-2 py-0.5 text-[11px] font-medium text-good">
                      <CheckCircle2 className="h-3 w-3" /> 啟用
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-full bg-ink-muted/10 px-2 py-0.5 text-[11px] font-medium text-ink-muted">
                      <XCircle className="h-3 w-3" /> 停用
                    </span>
                  )}
                </div>
                <Button variant="outline" size="sm" onClick={() => openEdit(env)} disabled={draft !== null}>
                  <Pencil className="h-3.5 w-3.5" /> 編輯
                </Button>
              </div>
              <dl className="grid grid-cols-1 gap-x-6 gap-y-1.5 text-xs sm:grid-cols-2">
                <ResourceRow label="Resource group" value={env.resourceGroup} />
                <ResourceRow label="bpm-svc App Service" value={env.bpmSvcApp} />
                <ResourceRow label="admin-svc App Service" value={env.adminSvcApp} />
                <ResourceRow label="bpm-ui Static Web App" value={env.bpmUiSwa} />
                <ResourceRow label="admin-ui Static Web App" value={env.adminUiSwa} />
              </dl>
            </Card>
          ))}
        </div>
      )}

      {/* Add / edit form */}
      {draft ? (
        <Card className="space-y-5 border-primary/40">
          <div className="flex items-center justify-between">
            <h2 className="text-base font-semibold text-ink">
              {isNew ? '新增環境' : `編輯環境：${draft.envName}`}
            </h2>
            <Button variant="ghost" size="sm" onClick={closeForm} disabled={saving}>
              <X className="h-3.5 w-3.5" /> 取消
            </Button>
          </div>

          <Field
            label="Env name / 環境名稱"
            required
            hint="此環境的唯一識別碼（upsert 的 key），如 poc / prod。建立後即為查找鍵。"
          >
            <Input
              value={draft.envName}
              onChange={(e) => setField('envName', e.target.value)}
              readOnly={!isNew}
              placeholder="e.g. poc"
            />
          </Field>

          <Field label="Resource group" hint="Azure 資源群組名稱，如 rg-poc。">
            <Input
              value={draft.resourceGroup}
              onChange={(e) => setField('resourceGroup', e.target.value)}
              placeholder="e.g. rg-poc"
            />
          </Field>

          <Field label="bpm-svc App Service name" hint="客戶端後端的 App Service 名稱，如 poc-flowcook-api。">
            <Input
              value={draft.bpmSvcApp}
              onChange={(e) => setField('bpmSvcApp', e.target.value)}
              placeholder="e.g. poc-flowcook-api"
            />
          </Field>

          <Field label="admin-svc App Service name" hint="管理端後端的 App Service 名稱，如 poc-flowcook-admin-api。">
            <Input
              value={draft.adminSvcApp}
              onChange={(e) => setField('adminSvcApp', e.target.value)}
              placeholder="e.g. poc-flowcook-admin-api"
            />
          </Field>

          <Field label="bpm-ui Static Web App name" hint="客戶端前端的 Static Web App 名稱，如 poc-flowcook-ui。">
            <Input
              value={draft.bpmUiSwa}
              onChange={(e) => setField('bpmUiSwa', e.target.value)}
              placeholder="e.g. poc-flowcook-ui"
            />
          </Field>

          <Field label="admin-ui Static Web App name" hint="管理端前端的 Static Web App 名稱，如 poc-flowcook-admin-ui。">
            <Input
              value={draft.adminUiSwa}
              onChange={(e) => setField('adminUiSwa', e.target.value)}
              placeholder="e.g. poc-flowcook-admin-ui"
            />
          </Field>

          <Checkbox
            id="deploy-enabled"
            label="啟用此環境（Enabled — 允許部署 worker 推送到此環境）"
            checked={draft.enabled}
            onChange={(e) => setField('enabled', e.target.checked)}
          />

          {formError && <p className="text-sm text-danger">{formError}</p>}

          <div className="flex items-center gap-3">
            <Button variant="primary" size="md" onClick={save} disabled={saving}>
              <Save className="h-4 w-4" /> {saving ? '儲存中…' : '儲存'}
            </Button>
          </div>
        </Card>
      ) : (
        <Button variant="outline" size="md" onClick={openAdd} disabled={loading}>
          <Plus className="h-4 w-4" /> 新增環境
        </Button>
      )}
    </div>
  )
}

function ResourceRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <dt className="text-ink-faint">{label}</dt>
      <dd className="font-mono text-ink">{value || <span className="text-ink-faint">—</span>}</dd>
    </div>
  )
}

function errMsg(e: unknown): string {
  if (e instanceof ApiError) return `HTTP ${e.status}: ${e.body}`
  if (e instanceof Error) return e.message
  return '未知錯誤'
}
