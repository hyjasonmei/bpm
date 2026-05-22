## Why

The BPM platform has nothing built yet. The partner brought back a 43-page PDF describing the workflows the target customers actually run today, and a working HTML prototype (TREND BPM-styled) covering 8 form types. To demo the platform — first to internal stakeholders, then to candidate SMEs — we need a single-page React frontend that:

- Looks credible at first glance (TREND BPM aesthetic the partner explicitly likes)
- Lets a demo presenter switch roles on the fly so the same shipment / form is shown from the requester's, manager's, finance's, and admin's points of view
- Renders the workflow position visually for each form (the chevron stepper from the prototype, plus a future BPMN diagram view)

The backend / workflow engine comes later. This change is **frontend only**, with mocked data, scaffolded so a later "wire the API" change is mostly mechanical.

## What Changes

- New project `bpm-ui/` (Vite + React 18 + TypeScript + Tailwind v4) at the repo root
- A small primitives layer (Button, Input, Select, Card, Badge, Stepper, etc.) hand-rolled in shadcn style — not the CLI; this matches the level of customization the design wants
- An `AppLayout` shell with the dark-navy top bar, BPM logo, and a left-aligned nav (Home / Search / User Guide / Report)
- A **Role Switcher** in the top-right that flips between five demo personas:
  - **Employee (Wilson You)** — submits, drafts, edits own
  - **Manager** — approves
  - **Finance** — runs FIN Review
  - **IT** — runs IT Spec Review
  - **HR** — runs onboarding / termination steps
  - **Admin** — sees everything, can configure
- Eight form screens carried over from the prototype:
  - Expense: GEE, GEV, APE
  - Travel: TRQ (read-only sample), TEO (read-only sample)
  - Purchase: HWP, ITPR (read-only sample)
  - HR: EXTOB (read-only sample)
- A **new ninth form**: **LEAVE (請假申請)** — the partner specifically mentioned this is the first end-to-end workflow they want to drive through the C# engine eventually
- A Workflow Stepper component that renders each form's current step with a gold/amber active state
- A `BpmnView` modal: opens an SVG diagram that mirrors the stepper but shows it as a BPMN-ish flow (start circle → tasks → gateways → end circle), drawn from the same step config so the two views can never disagree
- Three top-level pages mirroring the prototype HTMLs:
  - **Home** — greeting + stat cards + Pending My Action table + My Cases table + Activity Feed + Reminders + Quick Actions
  - **Search** — modal + dedicated page; filter by form type, status, requestor, date range
  - **Report** — basic charts (counts by type, status, monthly trend) over the same mock dataset
- Mock data covering ~30 cases across the form types and statuses, with a date range that makes the dashboard feel "live"

Out of scope for this change:
- Real workflow engine (it's planned, but C# backend lives in a sibling project / future change)
- Real BPMN editor (we render a static diagram per form for now)
- Authentication, API integration, persistence
- The remaining workflows from the PDF (Fixed Assets purchase / disposal / transfer / inventory) — they ship in a follow-up change
- Mobile breakpoints — desktop-first, demos run on a laptop

## Capabilities

### New Capabilities
- `bpm-ui-shell`: project layout, app shell, role switcher, navigation, top bar
- `bpm-form-stepper`: workflow step config + chevron stepper + BPMN diagram view
- `bpm-forms-expense`: GEE, GEV, APE forms (apply / approve / FIN review surfaces)
- `bpm-forms-travel`: TRQ, TEO forms
- `bpm-forms-purchase`: HWP, ITPR forms
- `bpm-forms-hr`: EXTOB form + new **LEAVE** form
- `bpm-home`: dashboard with role-aware widgets
- `bpm-search`: modal + page-based search
- `bpm-report`: form-volume charts

### Modified Capabilities
<!-- None — first change in the repo -->

## Impact

- New project: `bpm-ui/` (Vite, ~250-350KB gzipped JS once stocked with the form set)
- New deps: `react@18`, `tailwindcss@4`, `@tailwindcss/vite`, `class-variance-authority`, `clsx`, `tailwind-merge`, `lucide-react`. No charting library yet (Report uses CSS-only bars for the demo); a follow-up can pull in Recharts if needed.
- Mock data lives entirely in `src/lib/mocks.ts` so a later "wire the API" change deletes one file.
- No DB / backend impact.
- Demo path: open `http://localhost:5173`, click the role switcher to walk through the same shipment from each persona's perspective. The Leave form is the lighthouse demo for the workflow engine vision.
