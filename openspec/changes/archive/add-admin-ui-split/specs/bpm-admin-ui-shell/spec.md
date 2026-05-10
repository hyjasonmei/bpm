## ADDED Requirements

### Requirement: Admin UI is a separate vite project served at /admin

The system SHALL provide a second frontend project `bpm-admin-ui` deployed at sub-path `/admin/`. It MUST be a separate vite build, with its own `package.json`, `vite.config.ts`, and `dist/` output. It MUST share `bpm-shared` for common lib / types / UI primitives.

#### Scenario: Admin UI builds independently

- **WHEN** a developer runs `cd bpm-admin-ui && npm run build`
- **THEN** the build succeeds and produces `dist/` containing index.html, JS, CSS, all paths prefixed `/admin/`

#### Scenario: Employee UI builds independently

- **WHEN** a developer runs `cd bpm-ui && npm run build`
- **THEN** the build succeeds; no Onboarding screen code is present in the output bundle

### Requirement: Admin UI gates entry on admin role

On mount, `bpm-admin-ui` SHALL decode the JWT in `localStorage.bpm_jwt`. If the JWT is missing OR the `roles` claim does not contain `admin`, the UI SHALL render a "no permission" page and `location.replace('/app/')` after a 3-second countdown. Only when `admin` role is confirmed SHALL the admin layout render.

#### Scenario: Non-admin redirected away

- **GIVEN** a JWT for an employee (no admin role) is in localStorage
- **WHEN** the user navigates to `/admin/`
- **THEN** a "No permission" page renders with a 3-second countdown
- **AND** after 3 seconds the browser navigates to `/app/`

#### Scenario: Admin admitted

- **GIVEN** a JWT with `admin` role
- **WHEN** the user navigates to `/admin/`
- **THEN** the admin layout (sidebar + content) renders

### Requirement: bpm-shared package re-houses common code

The system SHALL provide an npm workspace package `bpm-shared` containing `lib/apiFetch`, `lib/cn`, all DTO type files (`types/*.ts`), and base UI primitives (`components/ui/*`). Both `bpm-ui` and `bpm-admin-ui` SHALL depend on it via `"file:../bpm-shared"` and import via `@bpm/shared`.

#### Scenario: Both UIs use shared apiFetch

- **GIVEN** `apiFetch` lives only in `bpm-shared`
- **WHEN** `bpm-ui/src/lib/api/hrFlows.ts` imports `apiFetch`
- **THEN** the import path is `@bpm/shared` (not `@/lib/apiFetch`)
- **AND** the same applies to `bpm-admin-ui` if it has any api client

#### Scenario: A change in bpm-shared affects both UIs

- **WHEN** a developer edits `bpm-shared/src/lib/apiFetch.ts`
- **AND** runs `npm run build:all` from root
- **THEN** both bpm-ui and bpm-admin-ui builds reflect the change without an intermediate publish step

### Requirement: Onboarding is removed from employee UI and lives in admin UI

The screens previously at `bpm-ui/src/screens/onboarding/**` SHALL be moved to `bpm-admin-ui/src/screens/onboarding/**`. The "Onboard" NavBtn SHALL be removed from `bpm-ui/src/components/AppLayout.tsx`. The `kind: 'onboarding'` variant SHALL be removed from `bpm-ui`'s Screen union. Persisted screen state (localStorage) referencing the removed kind SHALL be coerced to `home` on read.

#### Scenario: Employee nav has no Onboard button

- **WHEN** an employee loads `/app/`
- **THEN** the nav shows Home / Search / User Guide / Report / Attendance — no Onboard button

#### Scenario: Onboarding works from admin UI

- **WHEN** an admin clicks the Onboarding sidebar item in `/admin/`
- **THEN** the onboarding wizard renders identically to its prior behavior in bpm-ui
- **AND** completing the wizard still POSTs spec.json to `/api/spec`

#### Scenario: Stale localStorage screen kind handled

- **GIVEN** localStorage.bpm_screen = `{"kind":"onboarding"}` from a prior session
- **WHEN** the bpm-ui App loads
- **THEN** the screen is coerced to `{"kind":"home"}` (no error, no blank screen)

### Requirement: Backend CORS allows both UI origins

The backend `Cors:BpmUiOrigin` config value SHALL accept comma-separated origins. The CORS policy SHALL admit both `bpm-ui` and `bpm-admin-ui` origins (in dev: `http://localhost:5173,http://localhost:5174`; in prod: configured per deploy).

#### Scenario: Both origins accepted

- **GIVEN** config `BpmUiOrigin = "http://localhost:5173,http://localhost:5174"`
- **WHEN** a CORS preflight from either origin hits the backend
- **THEN** the response includes the matching `Access-Control-Allow-Origin` header

### Requirement: Cross-app navigation links

The employee UI's RoleSwitcher SHALL include an `🛠 Open Admin Console →` link visible only when the current user has `admin` role; clicking it navigates to `/admin/`. The admin UI's top bar SHALL include a `← Employee App` link that navigates to `/app/`. Both navigations SHALL preserve the JWT in localStorage so the destination UI does not require re-authentication.

#### Scenario: Admin sees console link

- **GIVEN** a JWT with admin role in bpm-ui
- **WHEN** the user opens the RoleSwitcher dropdown
- **THEN** an `🛠 Open Admin Console` row is present

#### Scenario: Employee does not see console link

- **GIVEN** a JWT without admin role
- **WHEN** the user opens the RoleSwitcher dropdown
- **THEN** no admin console link is shown

### Requirement: Workspace dev scripts

The root `package.json` SHALL provide:

- `npm run dev:ui` — starts bpm-ui only
- `npm run dev:admin` — starts bpm-admin-ui only
- `npm run dev` — starts both UIs concurrently (using `concurrently` or equivalent)
- `npm run build:all` — builds bpm-shared, bpm-ui, bpm-admin-ui in dependency order

#### Scenario: One-command dev

- **WHEN** a developer runs `npm run dev` from root
- **THEN** both bpm-ui and bpm-admin-ui dev servers start (ports 5173 and 5174)
