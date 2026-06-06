import { useEffect, useRef, useState } from 'react'
import { AlertTriangle, Check, Loader2, RotateCcw, X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { ApiError } from '@/flowcook/api'
import { useAuth } from '@/flowcook/auth/useAuth'
import { factoryWipeRuntime, reregisterFlows, reseedIdentity } from '@/flowcook/api/reset'

type StepState = 'idle' | 'running' | 'done' | 'error'
interface Step { key: string; label: string; state: StepState; detail?: string }

const CONFIRM_PHRASE = 'RESET'

const INITIAL_STEPS: Step[] = [
  { key: 'wipe', label: '清除 runtime 資料（案件 / 待辦 / 通知 / 攔信 / 稽核）', state: 'idle' },
  { key: 'reseed', label: '重新 seed 身分（使用者 / 部門 / 群組 / 角色 + 指派）', state: 'idle' },
  { key: 'flows', label: '重新註冊 + 發布全部流程', state: 'idle' },
]

export function ResetTab() {
  const { logout } = useAuth()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [phrase, setPhrase] = useState('')
  const [running, setRunning] = useState(false)
  const [steps, setSteps] = useState<Step[]>(INITIAL_STEPS)
  const [summary, setSummary] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const logoutTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => () => { if (logoutTimer.current) clearTimeout(logoutTimer.current) }, [])

  function setStep(key: string, state: StepState, detail?: string) {
    setSteps((prev) => prev.map((s) => (s.key === key ? { ...s, state, detail } : s)))
  }

  async function runReset() {
    setConfirmOpen(false)
    setRunning(true)
    setError(null)
    setSummary(null)
    setSteps(INITIAL_STEPS.map((s) => ({ ...s, state: 'idle', detail: undefined })))
    try {
      setStep('wipe', 'running')
      const wipe = await factoryWipeRuntime()
      setStep('wipe', 'done', `${wipe.casesDeleted} 案件 · ${wipe.capturedMessagesDeleted} 通知`)

      setStep('reseed', 'running')
      const seed = await reseedIdentity()
      setStep('reseed', 'done', `${seed.users} 使用者 · ${seed.roles} 角色`)

      setStep('flows', 'running')
      const flows = await reregisterFlows()
      setStep('flows', 'done', `${flows.registered.length} 流程已發布`)

      // Identity was rebuilt — every user (including the signed-in admin) has a
      // fresh id and the session table was cleared, so the current cookie is
      // now dead. Log out gracefully to the login page (demo creds prefilled)
      // instead of leaving a zombie session that 401s on the next click.
      setSummary('Demo 環境已重置回 seed 初始狀態。身分已重建，3 秒後回到登入頁…')
      logoutTimer.current = setTimeout(() => { void logout() }, 3000)
    } catch (e) {
      const msg = e instanceof ApiError ? `HTTP ${e.status}: ${e.body}` : e instanceof Error ? e.message : '未知錯誤'
      setSteps((prev) => prev.map((s) => (s.state === 'running' ? { ...s, state: 'error', detail: msg } : s)))
      setError(msg)
    } finally {
      setRunning(false)
      setPhrase('')
    }
  }

  const started = steps.some((s) => s.state !== 'idle')

  return (
    <div className="max-w-2xl">
      <div className="rounded-lg border border-amber-200 bg-amber-50 p-5">
        <div className="flex items-start gap-3">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" />
          <div>
            <h2 className="text-base font-semibold text-ink">重置 Demo 環境</h2>
            <p className="mt-1 text-sm text-ink-muted">
              一鍵把資料庫洗回 seed 初始狀態。會{' '}
              <strong className="text-amber-700">永久刪除所有流程案件、待辦、通知、攔信與稽核紀錄</strong>，
              並重新 seed 身分資料與重新發布全部流程。此操作無法復原。
            </p>
            <ul className="mt-3 space-y-1 text-xs text-ink-muted">
              <li>• 清除：所有流程的 case + runtime 資料</li>
              <li>• 重建：13 使用者 / 6 部門 / 1 群組 / 15 角色 + 指派</li>
              <li>• 重新註冊 + 發布全部流程</li>
            </ul>
          </div>
        </div>
      </div>

      {started && (
        <div className="mt-5 rounded-lg border border-rule bg-card p-4">
          <ol className="space-y-2.5">
            {steps.map((s, i) => (
              <li key={s.key} className="flex items-start gap-3 text-sm">
                <StepIcon state={s.state} index={i + 1} />
                <div className="min-w-0">
                  <div className={cn(s.state === 'error' ? 'text-danger' : 'text-ink')}>{s.label}</div>
                  {s.detail && (
                    <div className={cn('mt-0.5 text-xs', s.state === 'error' ? 'text-danger' : 'text-ink-muted')}>
                      {s.detail}
                    </div>
                  )}
                </div>
              </li>
            ))}
          </ol>
        </div>
      )}

      {summary && (
        <p className="mt-4 rounded border border-primary/30 bg-primary/5 px-3 py-2 text-sm text-ink">✓ {summary}</p>
      )}
      {error && (
        <p className="mt-4 rounded border border-danger/30 bg-danger/5 px-3 py-2 text-sm text-danger">重置失敗：{error}</p>
      )}

      <button
        onClick={() => { setConfirmOpen(true); setPhrase('') }}
        disabled={running}
        className="mt-5 inline-flex items-center gap-2 rounded-md border border-danger bg-danger px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-danger/90 disabled:opacity-50"
      >
        {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCcw className="h-4 w-4" />}
        {running ? '重置中…' : '重置 Demo 環境'}
      </button>

      {confirmOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-ink/30 p-4"
          onClick={() => setConfirmOpen(false)}
        >
          <div
            className="w-full max-w-md rounded-lg border border-rule bg-card p-6 shadow-xl"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center gap-2">
              <AlertTriangle className="h-5 w-5 text-danger" />
              <h3 className="text-base font-semibold text-ink">確認重置</h3>
            </div>
            <p className="mt-2 text-sm text-ink-muted">
              這會永久刪除所有案件並把資料庫 seed 回初始狀態，<strong className="text-danger">無法復原</strong>。 請輸入{' '}
              <code className="rounded bg-ink-muted/10 px-1.5 py-0.5 font-mono text-xs">{CONFIRM_PHRASE}</code> 以確認。
            </p>
            <input
              autoFocus
              value={phrase}
              onChange={(e) => setPhrase(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter' && phrase === CONFIRM_PHRASE) void runReset() }}
              placeholder={CONFIRM_PHRASE}
              className="mt-3 block w-full rounded border border-rule bg-white px-3 py-2 font-mono text-sm text-ink outline-none transition-colors placeholder:text-ink-faint focus:border-danger focus:ring-2 focus:ring-danger/20"
            />
            <div className="mt-4 flex justify-end gap-2">
              <button
                onClick={() => setConfirmOpen(false)}
                className="rounded px-3 py-1.5 text-sm text-ink-muted hover:text-ink"
              >
                取消
              </button>
              <button
                onClick={() => void runReset()}
                disabled={phrase !== CONFIRM_PHRASE}
                className="rounded-md bg-danger px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-danger/90 disabled:opacity-40"
              >
                確認重置
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function StepIcon({ state, index }: { state: StepState; index: number }) {
  if (state === 'running') return <Loader2 className="mt-0.5 h-4 w-4 shrink-0 animate-spin text-primary" />
  if (state === 'done') return <Check className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
  if (state === 'error') return <X className="mt-0.5 h-4 w-4 shrink-0 text-danger" />
  return (
    <span className="mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded-full border border-rule text-[10px] text-ink-faint">
      {index}
    </span>
  )
}
