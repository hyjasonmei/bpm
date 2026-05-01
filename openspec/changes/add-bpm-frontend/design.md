## Context

The partner has worked at Trend Micro and brought home both the **TREND BPM** product knowledge and a working HTML prototype that mirrors that look (dark-navy header, slate-100 page background, light-blue field labels, amber active workflow step, blue primary buttons). The partner explicitly said any visual cue from "TREND BPM" in the screenshots is the design language to copy.

Stakeholders:
- Partner (business / sales) — needs the demo to feel polished and familiar to the target buyer
- Owner (Jason) — solo dev, optimizes for "ship the demo fast, keep architecture migratable"
- Demo audience — SME stakeholders (50–300 head) who already use spreadsheet/email-based approval today

Constraints:
- Stack already chosen: React 18, Tailwind, shadcn-style primitives. No backend yet.
- The PDF lists 8+ workflows; the prototype has 8 forms; we add 1 more (Leave) the partner specifically wants. Keep the form-config-driven stepper so adding the 10th, 11th forms later is a one-line change.
- The end goal is a custom C# workflow engine where the stepper visualization is a side effect of the engine state. So the frontend needs a clean seam between "form definition" and "step state" so we can swap mock data for engine output.

## Goals / Non-Goals

**Goals:**
- TREND BPM look-and-feel is the visual default. No deviation, no "creative reinterpretation" — the partner has shown the screenshots, we copy.
- One **role switcher** in the top-right that swaps the active persona for the entire app. It changes:
  - Greeting text
  - Pending My Action list (different rows for Employee vs Manager vs Finance vs IT vs HR vs Admin)
  - Stat cards (counts of pending vs my drafts vs FIN review queue, etc.)
  - Form action buttons at the bottom (Submit vs Approve/Reject vs Finalize)
- Each form keeps its own step config and renders the same chevron stepper component
- A `BpmnView` modal renders the same step config as a BPMN-flavored SVG diagram (start → tasks → end). Single source of truth between stepper and diagram.
- Mock data is rich enough that switching roles on Home shows visibly different state (no empty tables in any role)
- New **Leave (請假) form** is the showcase: simplest workflow (Apply → Manager Approve → HR Record → Closed), four steps, tightest demo loop

**Non-Goals:**
- A real BPMN editor (we draw, not edit)
- Real authentication
- Mobile / responsive (desktop only for now)
- Internationalization beyond bilingual EN / 中文 inline labels (no i18n framework)
- Backend; no fetch in this change. All "save / submit" is local toast.

## Decisions

### Project layout

```
bpm-ui/
├── public/
│   └── brand/  (BPM logo SVG, plus optional TREND-BPM red mark)
├── src/
│   ├── components/
│   │   ├── ui/           # Button, Input, Select, Card, Badge, Stepper, ConfirmDialog
│   │   ├── AppLayout.tsx # top bar + role switcher + main slot
│   │   ├── BpmnView.tsx  # SVG diagram modal for any step config
│   │   └── RoleSwitcher.tsx
│   ├── screens/
│   │   ├── Home.tsx
│   │   ├── Search.tsx
│   │   ├── Report.tsx
│   │   ├── forms/
│   │   │   ├── LeaveForm.tsx          # NEW
│   │   │   ├── GEEForm.tsx
│   │   │   ├── GEVForm.tsx
│   │   │   ├── APEForm.tsx
│   │   │   ├── HWPForm.tsx
│   │   │   ├── ITPRView.tsx
│   │   │   ├── TRQView.tsx
│   │   │   ├── TEOView.tsx
│   │   │   └── EXTOBView.tsx
│   ├── lib/
│   │   ├── cn.ts
│   │   ├── mocks.ts
│   │   ├── workflow.ts   # step config types + helpers
│   │   └── role.ts       # active persona + permission matrix
│   ├── App.tsx
│   ├── main.tsx
│   └── index.css
├── index.html
├── tailwind.config.ts (only if needed; v4 mostly uses @theme in CSS)
├── tsconfig.json
└── vite.config.ts
```

### Workflow step config

Single source of truth — a const map keyed by form code:

```ts
type Step = { id: string; en: string; zh?: string }
type FormDef = {
  code: 'LEAVE' | 'GEE' | 'GEV' | 'APE' | 'TRQ' | 'TEO' | 'HWP' | 'ITPR' | 'EXTOB'
  label: string
  steps: Step[]
  initialActive: number  // for mock current state
  /** Map from step index to the persona that owns that step. */
  ownerByStep: PersonaCode[]
}
```

The Stepper takes `{ steps, activeStep }`. The BpmnView takes the same `{ steps, activeStep }` and renders an SVG with start/end circles plus step nodes. Active node is amber, completed nodes are slate-400 with a green check, future nodes are slate-300.

### Role switcher

Roles defined as:

```ts
type Persona = {
  id: 'employee' | 'manager' | 'finance' | 'it' | 'hr' | 'admin'
  displayName: string
  zhName: string
  defaultUser: { name: string; dept: string }
}
```

Stored in `localStorage` so refresh doesn't drop the persona during a demo. The active persona drives:

1. **`canAct(formDef, activeStep)` — permission gate** — returns true if persona owns that step OR is admin
2. **Home widgets** — pulls "pending my action" from a mock list filtered by persona
3. **Form action bar** — at step `s` for form `f`, render Submit if `s == 0 && persona == employee`, Approve/Reject if `persona == ownerByStep[s]`, otherwise read-only

The switcher itself is a dropdown in the top-right showing the current persona avatar + name. Clicking opens a panel with all six persona buttons.

**Why localStorage over a URL param**: deep linking is out of scope; persona is a demo affordance, not part of the URL.

### Mock data shape

`src/lib/mocks.ts` exports:
- `MOCK_CASES: Case[]` — ~30 cases across all form types, with `requestor`, `currentStep`, `status`, plus a `history` log entry list
- `getCasesForRole(persona)` — convenience filter
- `MOCK_USERS` — small directory used by approver dropdowns and "from / to" pickers
- `MOCK_DEPARTMENTS` — cost center options matching the prototype's TWT.1746G etc.

The data stays static across the session; "Submit" / "Approve" actions only update local state inside a screen and surface a toast — they don't mutate the source list. This keeps the demo idempotent across role switches.

### Visual system

Tokens in `src/index.css` `@theme` block:

| Token              | Value      | Use                                  |
| ------------------ | ---------- | ------------------------------------ |
| `--color-bg`       | `#F1F5F9`  | page background (slate-100)          |
| `--color-card`     | `#FFFFFF`  | card surfaces                        |
| `--color-header`   | `#1E2D3D`  | dark navy top bar (TREND BPM)        |
| `--color-accent`   | `#F59E0B`  | active workflow step (amber)         |
| `--color-primary`  | `#2563EB`  | primary action (Submit/Approve)      |
| `--color-danger`   | `#DC2626`  | reject / delete                      |
| `--color-good`     | `#16A34A`  | completed checks                     |
| `--color-rule`     | `#E2E8F0`  | hairline borders                     |
| `--color-label-bg` | `#F1F5F9`  | the light-blue field label background|
| `--font-sans`      | DM Sans + Noto Sans TC | bilingual           |
| `--font-mono`      | DM Mono    | request numbers, amounts             |

The font choice (DM Sans + DM Mono) is what the prototype uses. Keep it; do not deviate.

### Stepper rendering rule

Step is one of `{done, current, future}`. Render:

- `done` → slate-400 text, green check icon, no background
- `current` → amber bg, white text, rounded
- `future` → slate-400 text, no icon

Use chevron-right between steps. This matches the prototype byte-for-byte.

### Role permission matrix (mock)

| Form  | Step                 | Owner persona |
| ----- | -------------------- | ------------- |
| LEAVE | APPLY                | employee      |
| LEAVE | MANAGER APPROVE      | manager       |
| LEAVE | HR RECORD            | hr            |
| LEAVE | CLOSED               | —             |
| GEE   | APPLY                | employee      |
| GEE   | APPROVE              | manager       |
| GEE   | CONFIRM & PRINT      | finance       |
| GEE   | FIN REVIEW           | finance       |
| GEE   | CLOSE                | —             |
| HWP   | APPLY                | employee      |
| HWP   | IT SPEC REVIEW       | it            |
| HWP   | QUOTATION            | it            |
| HWP   | CONFIRM & PRINT      | manager       |
| HWP   | APPROVE              | manager       |
| HWP   | PO PROCESSING        | finance       |
| HWP   | CLOSE                | —             |

(Other forms follow the same pattern; defined in `src/lib/workflow.ts`.)

## Risks / Trade-offs

- **Building 9 forms in a single change is large.** Mitigation: ship the demo-critical ones (Leave, Home, GEE, EXTOB) first to make every screen feel "real", then port the remaining forms from the prototype JSX (already half-done). If we run out of time, the unfinished forms render a "Coming soon" placeholder behind the same nav entry.
- **Static mock data won't persist** across role switches. That's intentional (idempotent demo) but the partner may demo by clicking "Submit" expecting it to land in the Manager's queue. Mitigation: clearly label the toast "Demo: not persisted yet" and rely on pre-loaded mock cases that show the workflow at every step.
- **BPMN diagram is hand-rolled** (no `bpmn-js`). For more than ~7 nodes the layout starts to look cramped. Mitigation: stick to linear layouts for now; if a customer-facing demo needs a real BPMN editor we pull bpmn-js (~600KB) into a follow-up.
- **No backend** means refresh wipes state. Acceptable for the demo. Document in README.
- **Tailwind v4 zero-config** means certain plugins (forms, typography) don't auto-load; we replicate any plugin styles by hand. Already done in the prototype, will continue in the rewrite.

## Migration Plan

This is the first change in the repo, no migration needed.

When the C# workflow engine lands in a follow-up:
1. Replace `MOCK_CASES` reads with API calls to `/api/cases`
2. Replace local `useState` mutations on Submit/Approve with `POST /api/cases/:id/transition`
3. The step config (`workflow.ts`) becomes the *client* schema; the *server* owns its own copy. They stay in sync via a generated TS file from the C# engine's BPMN definition.

## Open Questions

- **Are the 5 demo personas enough?** Listed: employee, manager, finance, it, hr, admin. The PDF mentions Cost Center Owner (separate from manager?). Default for now: collapse Cost Center Owner into the manager persona. If the partner says otherwise, add a `cost-center-owner` persona — straightforward.
- **Leave types**: Annual / Sick / Personal / Marriage / Bereavement / Maternity / Paternity / Other. Defaulting to this Taiwan-standard set; partner can prune.
- **Approver assignment for Leave**: the prototype has hardcoded names (Elton Yang etc.). For Leave we'll pick `MOCK_USERS` whose role==manager and whose `dept` matches the requestor's. Good enough for demo.
- **Route shape**: hash router (`#/home`, `#/forms/leave`, `#/search`, `#/report`)? Or single-state-driven like the prototype? Defaulting to **single-state-driven**, matches the prototype, no router dep needed. Adds router later when deep-link demos become a need.
