## 1. Scaffold + tokens

- [ ] 1.1 `npm create vite@latest bpm-ui -- --template react-ts` in the repo root
- [ ] 1.2 `npm install` and verify the default Vite app boots
- [ ] 1.3 Add deps: `tailwindcss@4`, `@tailwindcss/vite`, `class-variance-authority`, `clsx`, `tailwind-merge`, `lucide-react`
- [ ] 1.4 Configure `vite.config.ts` with `tailwindcss()` plugin + `@/*` alias to `./src/*`
- [ ] 1.5 Replace `src/index.css` with `@import "tailwindcss"` + `@theme` block carrying the design tokens (header / accent / primary / good / danger / rule / label-bg / fonts)
- [ ] 1.6 Update `index.html` to load DM Sans + DM Mono + Noto Sans TC from Google Fonts and set page title `BPM System`
- [ ] 1.7 Add `paths` alias to `tsconfig.app.json`

## 2. UI primitives + helpers

- [ ] 2.1 `src/lib/cn.ts` (twMerge + clsx)
- [ ] 2.2 `src/components/ui/button.tsx` — primary / default / outline / ghost / destructive / amber variants
- [ ] 2.3 `src/components/ui/input.tsx`
- [ ] 2.4 `src/components/ui/select.tsx` (with chevron icon)
- [ ] 2.5 `src/components/ui/textarea.tsx`
- [ ] 2.6 `src/components/ui/checkbox.tsx`
- [ ] 2.7 `src/components/ui/badge.tsx` (status palette: default / success / warning / active / closed / returned)
- [ ] 2.8 `src/components/ui/card.tsx` + `SectionTitle` + `InfoBanner` + `TotalBar` + `UploadZone`
- [ ] 2.9 `src/components/ui/confirm-dialog.tsx` (re-use the pattern from ghp; required for any destructive button per project conventions)
- [ ] 2.10 `src/components/ui/req.tsx` + `FieldLabel`

## 3. Workflow + role

- [ ] 3.1 `src/lib/workflow.ts` defining `Step`, `FormDef`, `PersonaCode`; export `FORMS` map keyed by code with `LEAVE` first then GEE/GEV/APE/HWP/ITPR/TRQ/TEO/EXTOB
- [ ] 3.2 `ownerByStep` per form (per design.md table)
- [ ] 3.3 `src/lib/role.ts` defining `Persona`, `PERSONAS` constant array, `useActivePersona()` hook backed by localStorage
- [ ] 3.4 `canAct(formCode, activeStep, personaId)` helper
- [ ] 3.5 `src/components/Stepper.tsx` (chevron stepper consuming `{ steps, activeStep }`)
- [ ] 3.6 `src/components/BpmnView.tsx` (modal SVG diagram from same `{ steps, activeStep, ownerByStep }`)

## 4. App shell

- [ ] 4.1 `src/components/AppLayout.tsx` — top bar with logo, nav items, bell, help, RoleSwitcher
- [ ] 4.2 `src/components/RoleSwitcher.tsx` — dropdown with the 6 personas
- [ ] 4.3 `src/App.tsx` — single-state router (`screen: 'home' | 'search' | 'report' | 'form/<code>'`)
- [ ] 4.4 `localStorage` keys: `bpm_active_role`, `bpm_screen`

## 5. Mock data

- [ ] 5.1 `src/lib/mocks.ts` — MOCK_USERS (10 users with role tags), MOCK_DEPARTMENTS, MOCK_CASES (~30 cases across 9 form types, varied statuses, varied requestors), MOCK_ACTIVITY, MOCK_REMINDERS, MOCK_LEAVE_BALANCES
- [ ] 5.2 Helper functions: `casesPendingForPersona(persona)`, `casesCreatedBy(userId)`, `caseByNo(no)`

## 6. Home screen

- [ ] 6.1 `src/screens/Home.tsx` — greeting + 4 stat cards + grid (left: PendingTable + MyCasesTable; right: QuickActions + ActivityFeed + Reminders)
- [ ] 6.2 Stat-card content swaps per persona
- [ ] 6.3 PendingTable rows are persona-filtered with "Open" buttons that route to the form screen
- [ ] 6.4 ActivityFeed component (icon + actor + verb + doc chip + relative time)
- [ ] 6.5 RemindersPanel component

## 7. Forms — top priority

- [ ] 7.1 **`src/screens/forms/LeaveForm.tsx`** — leave type / dates / total days / reason / proxy approver / attachment + bottom action bar (persona-driven)
- [ ] 7.2 LeaveForm shows annual-leave balance for HR persona (read-only) sourced from `MOCK_LEAVE_BALANCES`
- [ ] 7.3 `src/screens/forms/GEEForm.tsx` — port from prototype JSX, replace inline-styled vanilla React with Tailwind + TS
- [ ] 7.4 `src/screens/forms/EXTOBView.tsx` — port from `ReadOnlyViews.jsx`

## 8. Forms — secondary (placeholder if time-constrained)

- [ ] 8.1 `GEVForm.tsx` (vendor + VAT)
- [ ] 8.2 `APEForm.tsx` (advance payment)
- [ ] 8.3 `HWPForm.tsx` (hardware purchase)
- [ ] 8.4 `ITPRView.tsx` (read-only IT purchase request)
- [ ] 8.5 `TRQView.tsx` (read-only travel request)
- [ ] 8.6 `TEOView.tsx` (read-only travel expense)

## 9. Search + Report

- [ ] 9.1 `src/screens/Search.tsx` — full-page search with the same filter set as the modal
- [ ] 9.2 `src/components/SearchModal.tsx` — modal version, opened from top nav
- [ ] 9.3 Filters: keyword / req no. / form types / statuses / requestor / date range
- [ ] 9.4 Pagination: 10/25/50 per page, first/prev/next/last
- [ ] 9.5 `src/screens/Report.tsx` — three views (counts by type bar, counts by status stacked, monthly volume line) using CSS-only bars

## 10. Polish + verify + ship

- [ ] 10.1 `npx tsc -b` clean
- [ ] 10.2 `npx vite build` clean
- [ ] 10.3 `npm run dev` smoke test — open every screen with at least 2 different personas
- [ ] 10.4 Initial git commit on `main` (project is a fresh git repo with no commits yet)
- [ ] 10.5 Push `main` to GitHub if a remote is configured; otherwise leave a note for the owner to add the remote
