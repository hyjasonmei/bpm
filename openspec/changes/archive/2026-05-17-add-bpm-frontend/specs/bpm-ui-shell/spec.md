## ADDED Requirements

### Requirement: App shell and navigation

The system SHALL provide a single-page React 18 application served by Vite at `bpm-ui/`. The shell SHALL include a sticky top bar with: BPM monogram + product name on the left, primary navigation (Create / Home / Search / User Guide / Report) center-left, and a notifications bell + help icon + role-switcher dropdown on the right. The page background SHALL be `slate-100`. The top bar background SHALL be the dark-navy `#1E2D3D` per TREND BPM.

#### Scenario: Top bar renders on every screen
- **WHEN** the user lands on any screen (Home / Search / Report / any form)
- **THEN** the top bar with logo, nav, and role switcher is visible at the top of the viewport

#### Scenario: Active nav item is highlighted
- **WHEN** the user is on a Home / Search / Report page
- **THEN** that nav item shows the active state (white background at 20% opacity)

### Requirement: Role switcher

The system SHALL provide a role-switcher control in the top-right that selects exactly one active persona from the set `{ employee, manager, finance, it, hr, admin }`. The selection SHALL persist in `localStorage` under key `bpm_active_role` so a page refresh during a demo does not lose the persona. Switching the persona SHALL re-render any view that depends on it without a page reload.

#### Scenario: Role persists across reload
- **WHEN** the user selects "Manager" then reloads the page
- **THEN** the top-right indicator still shows "Manager" and Home shows the manager view

#### Scenario: Switching role updates Home immediately
- **WHEN** the user is on Home as Employee, then switches to Manager
- **THEN** the greeting text, stat cards, and Pending My Action table update to reflect the manager view without a network round trip

#### Scenario: Six personas are selectable
- **WHEN** the user opens the role switcher dropdown
- **THEN** six persona entries are listed: Employee, Manager, Finance, IT, HR, Admin

### Requirement: Form-action buttons reflect persona

Each form SHALL render its bottom action bar based on the active persona's permission for the current step. If the persona owns the current step, the persona-appropriate action(s) SHALL be shown (e.g., Submit for an Employee on APPLY; Approve / Reject for a Manager on APPROVE; Confirm for Finance on FIN REVIEW). If the persona does not own the current step, the action bar SHALL render a read-only banner with a "View only" label.

#### Scenario: Employee on APPLY step sees Submit
- **WHEN** the active persona is Employee and the form's current step is APPLY
- **THEN** the bottom bar shows a "Save as Draft" button and a primary "Submit" button

#### Scenario: Manager on APPROVE step sees Approve / Reject
- **WHEN** the active persona is Manager and the form's current step is APPROVE
- **THEN** the bottom bar shows "Reject" (destructive) and a primary "Approve" button

#### Scenario: Wrong persona sees view-only banner
- **WHEN** the active persona is Employee and the form's current step is APPROVE
- **THEN** no action buttons render and a read-only banner reads "Awaiting Manager approval / 等候主管簽核"

### Requirement: Visual design tokens

The system SHALL define design tokens in `src/index.css` mapping to TREND BPM-style colors: `--color-header #1E2D3D`, `--color-accent #F59E0B`, `--color-primary #2563EB`, `--color-good #16A34A`, `--color-danger #DC2626`, `--color-rule #E2E8F0`. Fonts SHALL be DM Sans (sans) + DM Mono (monospace) loaded from Google Fonts, paired with Noto Sans TC for Chinese content.

#### Scenario: Tokens are reachable from any component
- **WHEN** a component uses `bg-accent` or `text-primary`
- **THEN** the resolved color matches the tokens above
