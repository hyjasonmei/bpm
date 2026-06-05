import { useEffect, useState } from 'react'
import { createBrowserRouter, Navigate, Outlet, useNavigate, useParams } from 'react-router-dom'

import { AppLayout } from '@/components/AppLayout'
import { Attendance } from '@/screens/Attendance'
import { CaseDetail } from '@/screens/CaseDetail'
import { CreateIndex } from '@/screens/CreateIndex'
import { Home } from '@/screens/Home'
import { Search } from '@/screens/Search'
import { NotCookedYet } from '@/screens/forms/NotCookedYet'
import { lookupForm } from '@/features/registry'
import type { FormCode } from '@/lib/workflow'
import { FORMS } from '@/lib/workflow'
import { useActivePersona } from '@/lib/role'
import { getTask } from '@/lib/api/process'

/**
 * SPA route map (introduced by `add-spa-routing`). The URL is the source of
 * truth for navigation; the old `Screen` discriminated union is retired.
 *
 * Form mode is URL-driven: `/apply/:code` opens the form in create mode,
 * `/tasks/:taskId` opens it in task mode (after looking up the task's
 * specCode). Chef-cooked form components keep their existing prop shape
 * (`persona`, `mode`, `taskId`, `onSubmitted`) — only the wiring changes.
 */
export const router = createBrowserRouter([
  {
    path: '/',
    element: <ShellRoute />,
    children: [
      { index: true, element: <HomeRoute /> },
      { path: 'create', element: <CreateIndex /> },
      { path: 'search', element: <Search /> },
      { path: 'attendance', element: <Attendance /> },
      { path: 'cases/:flowCode/:caseId', element: <FeatureCaseDetailRoute /> },
      { path: 'cases/:instanceId', element: <CaseDetailRoute /> },
      { path: 'apply/:code', element: <FormRoute mode="create" /> },
      { path: 'tasks/:taskId', element: <FormRoute mode="task" /> },
      // 404 → home
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
])

function ShellRoute() {
  // AppLayout reads URL state internally (NavLink); persona / auth comes from
  // the existing useActivePersona hook so the rest of the tree stays familiar.
  const { code: persona, setCode: setPersona, authedUser, pending: authPending, error: authError } = useActivePersona()
  return (
    <AppLayout
      persona={persona}
      setPersona={setPersona}
      authedFullName={authedUser?.fullName ?? null}
      authPending={authPending}
      authError={authError}
    >
      <Outlet />
    </AppLayout>
  )
}

function HomeRoute() {
  const { code: persona } = useActivePersona()
  return <Home persona={persona} />
}

function CaseDetailRoute() {
  const { instanceId } = useParams()
  if (!instanceId) return <Navigate to="/" replace />
  return <CaseDetail instanceId={instanceId} />
}

function FeatureCaseDetailRoute() {
  const { flowCode, caseId } = useParams()
  const { code: persona } = useActivePersona()
  if (!flowCode || !caseId) return <Navigate to="/" replace />
  // The URL slug is the lowercased flow code; normalize back to the canonical
  // FormCode. Some chef-cooked flows emit a hyphenated slug in their detailUrl /
  // post-submit redirect (e.g. `purchase-request`) even though the FormCode uses
  // an underscore (`PURCHASE_REQUEST`), so map `-` → `_` as well as uppercasing.
  // FormCodes never contain a hyphen, so this is a safe normalization.
  const normalizedCode = flowCode.toUpperCase().replace(/-/g, '_')
  const manifest = lookupForm(normalizedCode as FormCode)
  if (!manifest?.detailComponent) {
    return <div className="mx-auto max-w-md p-8 text-sm text-ink-muted">
      {normalizedCode} 還沒提供 case detail view（chef ship `detailComponent` in manifest）.
    </div>
  }
  const Detail = manifest.detailComponent
  return <Detail caseId={caseId} persona={persona} />
}

interface FormRouteProps {
  mode: 'create' | 'task'
}

function FormRoute({ mode }: FormRouteProps) {
  const { code, taskId } = useParams()
  const { code: persona } = useActivePersona()
  const navigate = useNavigate()

  // Create mode: FormCode is the URL param.
  if (mode === 'create') {
    if (!code) return <Navigate to="/" replace />
    return <RenderForm formCode={code as FormCode} persona={persona} mode="create" taskId={null} onSubmitted={() => navigate('/')} />
  }

  // Task mode: look up specCode by fetching the task, then mount the form.
  if (!taskId) return <Navigate to="/" replace />
  return <FormTaskLoader taskId={taskId} persona={persona} onSubmitted={() => navigate('/')} />
}

function FormTaskLoader({ taskId, persona, onSubmitted }: {
  taskId: string
  persona: ReturnType<typeof useActivePersona>['code']
  onSubmitted: () => void
}) {
  const [specCode, setSpecCode] = useState<FormCode | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setSpecCode(null)
    setError(null)
    void (async () => {
      try {
        const t = await getTask(taskId)
        if (!cancelled) setSpecCode(t.specCode as FormCode)
      } catch (e: unknown) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      }
    })()
    return () => { cancelled = true }
  }, [taskId])

  if (error) {
    return <div className="mx-auto max-w-md p-8 text-sm text-danger">Couldn't load task {taskId}: {error}</div>
  }
  if (!specCode) {
    return <div className="mx-auto max-w-md p-8 text-sm text-ink-muted">Loading task…</div>
  }
  return <RenderForm formCode={specCode} persona={persona} mode="task" taskId={taskId} onSubmitted={onSubmitted} />
}

function RenderForm({ formCode, persona, mode, taskId, onSubmitted }: {
  formCode: FormCode
  persona: ReturnType<typeof useActivePersona>['code']
  mode: 'create' | 'task'
  taskId: string | null
  onSubmitted: () => void
}) {
  const manifest = lookupForm(formCode)
  if (!manifest) return <NotCookedYet code={formCode} />
  const Form = manifest.component
  return <Form persona={persona} mode={mode} taskId={taskId} onSubmitted={onSubmitted} />
}

/** Convenience helpers for callers that don't want to hardcode URL strings. */
export const routes = {
  home: () => '/',
  create: () => '/create',
  search: () => '/search',
  attendance: () => '/attendance',
  caseDetail: (instanceId: string) => `/cases/${instanceId}`,
  formCreate: (code: FormCode) => `/apply/${code}`,
  formTask: (taskId: string) => `/tasks/${taskId}`,
} as const

// Keep an export so FORMS isn't pruned (used by FormSubHeader inside AppLayout).
void FORMS
