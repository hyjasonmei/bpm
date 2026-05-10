# Tasks

## 1. Workspace setup

- [ ] 1.1 Create root `package.json` with `"workspaces": ["bpm-ui", "bpm-admin-ui", "bpm-shared"]`
- [ ] 1.2 Add convenience scripts: `npm run dev:ui`, `npm run dev:admin`, `npm run dev` (concurrently both), `npm run build:all`
- [ ] 1.3 Add `concurrently` as devDep at root if going with parallel dev
- [ ] 1.4 Document workspace usage in root README

## 2. bpm-shared package

- [ ] 2.1 Create `bpm-shared/package.json` (name `@bpm/shared`, type module, main entry `./src/index.ts`)
- [ ] 2.2 Create `bpm-shared/tsconfig.json` (composite project)
- [ ] 2.3 Create `bpm-shared/src/index.ts` re-exporting all public modules
- [ ] 2.4 Move `bpm-ui/src/lib/apiFetch.ts` → `bpm-shared/src/lib/apiFetch.ts`
- [ ] 2.5 Move `bpm-ui/src/lib/cn.ts` → `bpm-shared/src/lib/cn.ts`
- [ ] 2.6 Move `bpm-ui/src/types/*.ts` → `bpm-shared/src/types/`
- [ ] 2.7 Move `bpm-ui/src/components/ui/*` → `bpm-shared/src/components/ui/`
- [ ] 2.8 Add `@bpm/shared` to `bpm-ui/package.json` deps as `"file:../bpm-shared"`
- [ ] 2.9 Update all bpm-ui imports from `@/lib/apiFetch` etc. to `@bpm/shared`
- [ ] 2.10 Verify bpm-ui still builds + typechecks

## 3. bpm-admin-ui skeleton

- [ ] 3.1 Scaffold new vite + react + TS project at `bpm-admin-ui/`
- [ ] 3.2 Tailwind config (copy from bpm-ui), set distinct primary color (e.g., slate/zinc) to visually differ
- [ ] 3.3 `package.json`: same React/Vite versions as bpm-ui; depend on `@bpm/shared`
- [ ] 3.4 `vite.config.ts` with `base: '/admin/'`, port 5174, proxy `/api/*` to backend
- [ ] 3.5 `tsconfig.app.json` mirror bpm-ui's
- [ ] 3.6 `index.html` with title "BPM Admin Console"

## 4. bpm-admin-ui shell

- [ ] 4.1 `src/main.tsx` standard React entry
- [ ] 4.2 `src/App.tsx` with admin role guard:
  - Read JWT from `bpm_jwt`
  - Decode `roles` claim; if no `admin` → render `<NoPermission />` and after 3s `location.replace('/app/')`
  - Otherwise render `<AdminLayout>`
- [ ] 4.3 `src/components/AdminLayout.tsx`:
  - Left sidebar with sections: Onboarding, Site Settings (placeholder), Users & Roles (placeholder), Impersonation (placeholder), Audit Logs (placeholder)
  - Top bar with: BPM Admin logo, current admin user, Logout
- [ ] 4.4 `src/components/NoPermission.tsx` — friendly 403 page with countdown
- [ ] 4.5 Wire dev-mode RoleSwitcher (admin persona auto-login if no JWT in dev mode)

## 5. Move Onboarding

- [ ] 5.1 Move `bpm-ui/src/screens/onboarding/**` → `bpm-admin-ui/src/screens/onboarding/**`
- [ ] 5.2 Move `bpm-ui/src/lib/onboarding.ts` → `bpm-admin-ui/src/lib/onboarding.ts` (assuming admin-only)
- [ ] 5.3 Move `bpm-ui/src/lib/onboardingTools.ts` → `bpm-admin-ui/src/lib/`
- [ ] 5.4 Decide bpmnXml.ts / bpmnXmlParse.ts ownership: if BpmnView is also used in employee form ActionBar, leave bpmnXml in bpm-shared; otherwise move
- [ ] 5.5 Wire Onboarding screen into AdminLayout sidebar
- [ ] 5.6 Verify the wizard still works end-to-end (start flow, finish, drop spec.json to /api/spec)

## 6. Purge Onboarding from bpm-ui

- [ ] 6.1 Remove `Onboard` NavBtn from `bpm-ui/src/components/AppLayout.tsx`
- [ ] 6.2 Remove `case 'onboarding'` from App.tsx Screen union
- [ ] 6.3 Remove import + render of Onboarding component
- [ ] 6.4 Remove orphaned files left behind by the move
- [ ] 6.5 If user has `localStorage.bpm_screen` set to `{kind:'onboarding'}` — coerce to `home` on read in App.tsx (one-time migration)

## 7. Backend CORS

- [ ] 7.1 Update `appsettings.json` and `appsettings.Development.json`: `"BpmUiOrigin": "http://localhost:5173,http://localhost:5174"`
- [ ] 7.2 Verify Program.cs CORS handling already splits comma (it does per current code)
- [ ] 7.3 Document for prod: both UI origins must be in env config

## 8. Cross-app navigation

- [ ] 8.1 In bpm-ui's RoleSwitcher (when admin role detected): add an `🛠 Open Admin Console →` link that opens `/admin/` in new tab (or same tab, configurable)
- [ ] 8.2 In bpm-admin-ui top bar: add `← Employee App` link back to `/app/`

## 9. Update dogfood + dev tooling

- [ ] 9.1 Update `dogfood.command` to start backend + both UIs (or document the new flow)
- [ ] 9.2 Update root README with project structure diagram

## 10. Verify

- [ ] 10.1 `cd bpm-shared && npx tsc --build` clean
- [ ] 10.2 `cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit && npm run build` clean
- [ ] 10.3 `cd bpm-admin-ui && npx tsc -p tsconfig.app.json --noEmit && npm run build` clean
- [ ] 10.4 Manual: load `http://localhost:5173` as employee → no Onboard button in nav
- [ ] 10.5 Manual: load `http://localhost:5173` as admin → Open Admin Console link visible
- [ ] 10.6 Manual: navigate to `http://localhost:5174` as employee → 403 redirect
- [ ] 10.7 Manual: navigate to `http://localhost:5174` as admin → admin UI loads, Onboarding workable
- [ ] 10.8 Screenshot both UIs side by side for `dogfood-screenshots/`
