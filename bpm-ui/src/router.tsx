import { createBrowserRouter, Navigate, Outlet, useNavigate, useParams } from 'react-router-dom'

import { AppLayout } from '@/components/AppLayout'
import { CaseToolbar } from '@/components/CaseToolbar'
import { Attendance } from '@/screens/Attendance'
import { AttendanceCorrectionReview } from '@/screens/AttendanceCorrectionReview'
import { CreateIndex } from '@/screens/CreateIndex'
import { Home } from '@/screens/Home'
import { Search } from '@/screens/Search'
import DatasetDemo from '@/screens/DatasetDemo'
import { NotCookedYet } from '@/screens/forms/NotCookedYet'
import { lookupForm } from '@/features/registry'
import type { FormCode } from '@/lib/workflow'
import { FORMS } from '@/lib/workflow'
import { useActivePersona } from '@/lib/role'

/**
 * SPA route map. The URL is the source of truth for navigation.
 *
 * `/apply/:code` opens a flow's form in create mode; a case opens at
 * `/cases/:flowCode/:caseId` (the chef-cooked detail component, which hosts
 * its own decision actions). The model-A generic `/cases/:instanceId` +
 * `/tasks/:taskId` routes were removed with the process runtime.
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
      { path: 'attendance/corrections/:id', element: <AttendanceCorrectionReview /> },
      // Version-qualified case detail (e.g. /cases/wfh/v1/<id>) — a historical
      // case opens ITS version's detail component. The bare 2-segment form is
      // kept for back-compat (old links / current-version cases) and resolves
      // to the highest version.
      { path: 'cases/:flowCode/:version/:caseId', element: <FeatureCaseDetailRoute /> },
      { path: 'cases/:flowCode/:caseId', element: <FeatureCaseDetailRoute /> },
      { path: 'apply/:code', element: <FormRoute /> },
      // dev-only: custom-dataset cascading demo (stripped from prod builds)
      ...(import.meta.env.DEV ? [{ path: 'dataset-demo', element: <DatasetDemo /> }] : []),
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

function FeatureCaseDetailRoute() {
  const { flowCode, version, caseId } = useParams()
  const { code: persona } = useActivePersona()
  if (!flowCode || !caseId) return <Navigate to="/" replace />
  // The URL slug is the lowercased flow code; normalize back to the canonical
  // FormCode. Some chef-cooked flows emit a hyphenated slug in their detailUrl /
  // post-submit redirect (e.g. `purchase-request`) even though the FormCode uses
  // an underscore (`PURCHASE_REQUEST`), so map `-` → `_` as well as uppercasing.
  // FormCodes never contain a hyphen, so this is a safe normalization.
  const normalizedCode = flowCode.toUpperCase().replace(/-/g, '_')
  // `version` is the optional 3rd segment ("v1"). Resolve THAT version's detail
  // so an old case hits its own API; absent → highest (back-compat).
  const parsedVersion = version ? Number.parseInt(version.replace(/^v/i, ''), 10) : NaN
  const manifest = lookupForm(normalizedCode as FormCode, Number.isFinite(parsedVersion) ? parsedVersion : undefined)
  if (!manifest?.detailComponent) {
    return <div className="mx-auto max-w-md p-8 text-sm text-ink-muted">
      {normalizedCode} 還沒提供 case detail view（chef ship `detailComponent` in manifest）.
    </div>
  }
  const Detail = manifest.detailComponent
  return (
    <div className="relative">
      {/* Copy-link + Print overlaid onto the per-flow header row, just left
          of its "View BPMN" button. Overlaid (not in normal flow) so chef's
          CaseDetail is untouched; the container mirrors the detail's
          `max-w-screen-lg` + `p-6` so the right edge lines up, and the
          ~132px right offset clears the View BPMN button + gap (every flow
          cribs the same header from LEAVE V1). pointer-events-none lets
          clicks fall through to the detail (incl. View BPMN) everywhere
          except the toolbar itself. */}
      <div className="pointer-events-none absolute inset-x-0 top-0 z-10">
        <div className="mx-auto flex max-w-screen-lg justify-end pl-6 pr-[156px] pt-[30px]">
          <CaseToolbar />
        </div>
      </div>
      <Detail caseId={caseId} persona={persona} />
    </div>
  )
}

function FormRoute() {
  const { code } = useParams()
  const { code: persona } = useActivePersona()
  const navigate = useNavigate()
  if (!code) return <Navigate to="/" replace />
  const manifest = lookupForm(code as FormCode)
  if (!manifest) return <NotCookedYet code={code as FormCode} />
  const Form = manifest.component
  return <Form persona={persona} mode="create" taskId={null} onSubmitted={() => navigate('/')} />
}

/** Convenience helpers for callers that don't want to hardcode URL strings. */
export const routes = {
  home: () => '/',
  create: () => '/create',
  search: () => '/search',
  attendance: () => '/attendance',
  caseDetail: (flowCode: string, caseId: string, version?: number) =>
    version != null
      ? `/cases/${flowCode.toLowerCase()}/v${version}/${caseId}`
      : `/cases/${flowCode.toLowerCase()}/${caseId}`,
  formCreate: (code: FormCode) => `/apply/${code}`,
} as const

// Keep an export so FORMS isn't pruned (used by FormSubHeader inside AppLayout).
void FORMS
