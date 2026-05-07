## Why

inovation_idea.md states "Mobile app out of scope, web responsive only initially". But the current bpm-ui is desktop-only:

- 9 mock-up forms have fixed-width layouts; squeeze unusable below 768 px
- AppLayout sidebar always visible; eats most width on phones
- Tables (Search, Reports) overflow horizontally with no smart strategy
- DynamicForm (`add-form-runtime-rendering`) has no mobile-aware controls

For SME use case: managers approve on phones during 開會 / 出差 / 接小孩 — mobile responsiveness is table stakes. Without it, the tool stays at the desk and adoption suffers.

## What Changes

### Mobile responsive capability (NEW `bpm-mobile-responsive`)

Breakpoints (Tailwind defaults already in package.json):

- `< 640px`: phone
- `640-1024px`: tablet
- `≥ 1024px`: desktop (current default)

### AppLayout responsive

- Phone: sidebar collapses to a hamburger drawer; top bar shows logo + hamburger + Bell + RoleSwitcher
- Tablet: sidebar narrow icons-only; expand on hover
- Desktop: as today

### TaskExecution mobile

- Stepper sidebar collapses to a horizontal tracker on top
- Form fields stack vertically (already do due to flexbox)
- Repeater table converts to card list on phone (each row a card with sub-fields stacked)
- Comment thread fills width; reply input is a fixed bottom sheet

### Forms (mock-ups) responsive

- The 9 hand-coded forms gain responsive Tailwind classes (`md:`, `lg:` prefixes)
- Two-column field grids on desktop become single-column on phone
- Action buttons sticky at bottom on phone (no scrolling to find Submit)

This change DOES touch the 9 mock-up forms — but only the layout (Tailwind class additions). The underlying logic / fields / strings unchanged. Demo runs continue to work; on phone they finally render usable.

### Dashboards (admin / process / reports) responsive

- Tables → horizontal scroll wrapper with sticky first column on phone
- Charts (recharts) auto-resize to viewport
- Filters sidebar → bottom sheet on phone

### Touch-friendly hit targets

- Minimum 44x44 px tap targets per Apple HIG / Google MD
- Spacing between adjacent buttons increased on phone
- Drag-and-drop disabled on touch (replaced with explicit move buttons in admin tree)

### Out of scope (future changes)

- Native mobile app (React Native / Capacitor) — separate project
- Offline mode (service worker for offline form fill)
- Push notifications via service worker (use email + in-app polling for now)
- Mobile-specific shortcuts / gestures
- Voice / camera integrations
- Tablet split-view layouts

## Capabilities

### New Capabilities

- `bpm-mobile-responsive` — responsive AppLayout, mobile-aware DynamicForm rendering (repeater as cards on phone), responsive 9 mock-up forms, responsive dashboards / tables / charts, touch hit-target sizing.

### Modified Capabilities

- None.

## Impact

- **bpm-ui/src/components/AppLayout.tsx**: hamburger drawer + responsive sidebar
- **bpm-ui/src/components/MobileSidebar.tsx**: NEW — drawer
- **bpm-ui/src/screens/forms/*.tsx**: layout class additions ONLY (no logic change, demo preserved)
- **bpm-ui/src/screens/Home.tsx, Search.tsx, Report.tsx**: layout class additions
- **bpm-ui/src/screens/TaskExecution.tsx**: Stepper top tracker on phone
- **bpm-ui/src/components/form-runtime/RepeaterField.tsx**: card-list on phone
- **bpm-ui/src/components/charts/**: ensure recharts responsive containers
- **bpm-ui/src/index.css**: viewport meta + base mobile styles
- **No backend changes**
- **No new NPM dependencies**
- **Demo guard**: 9 mock-up forms LOGIC NOT modified — only Tailwind class additions for responsive layout. Visual at desktop breakpoint stays byte-identical
