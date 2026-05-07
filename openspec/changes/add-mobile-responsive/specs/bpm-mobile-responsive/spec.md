## ADDED Requirements

### Requirement: AppLayout adapts to viewport

The `AppLayout` component SHALL render different chrome based on viewport width:

- `< 640px` (phone): top bar with hamburger + Bell + RoleSwitcher; sidebar hidden behind drawer
- `640-1024px` (tablet): narrow icon-only sidebar with hover-expand
- `≥ 1024px` (desktop): full sidebar (current default)

The hamburger toggle SHALL open a slide-in drawer with the full sidebar contents. Closing via outside-click, escape key, or close button.

#### Scenario: Phone shows hamburger

- **GIVEN** viewport is 375 px wide
- **WHEN** the AppLayout mounts
- **THEN** the top bar shows hamburger; sidebar is hidden

#### Scenario: Desktop unchanged

- **GIVEN** viewport is 1280 px wide
- **WHEN** the AppLayout mounts
- **THEN** the layout is byte-identical to pre-change

### Requirement: RepeaterField renders as cards on phone

`<RepeaterField>` SHALL render its rows as a `<table>` on viewports ≥ 640px, and as a vertical card list on phone (< 640px). Each card stacks the sub-fields with labels above values, plus a Remove button at the bottom.

#### Scenario: Phone repeater is cards

- **GIVEN** the field has 3 rows
- **WHEN** rendered on phone
- **THEN** the DOM contains 3 stacked card `<div>` elements (no `<table>`)

### Requirement: TaskExecution layout stacks on phone

On phone viewports, the TaskExecution screen SHALL stack vertically:

- Top: Stepper rendered as a horizontal pill tracker (current node highlighted)
- Middle: form
- Bottom: sticky action bar with Submit / Approve / Reject / Return buttons

The desktop layout (sidebar Stepper + main form) is unchanged on viewports ≥ 1024px.

#### Scenario: Phone stacked layout

- **WHEN** TaskExecution renders on phone
- **THEN** Stepper appears at top; form below; action bar fixed to bottom

### Requirement: Mock-up form layouts responsive at < 640px

The 9 mock-up form components (LeaveForm, GEEForm, GEVForm, APEForm, TRQForm, TEOForm, HWPForm, ITPRForm, EXTOBForm) SHALL include responsive Tailwind classes (`md:`, `lg:` prefixes) so:

- < 640px: single-column field layout, sticky bottom action buttons
- 640-1024px: two-column where appropriate
- ≥ 1024px: existing layout preserved BYTE-IDENTICAL

This change SHALL only add Tailwind classes; no JSX restructure, no logic change. The 1280-px desktop demo experience SHALL remain unchanged.

#### Scenario: Desktop demo unchanged

- **GIVEN** viewport at 1280x720 (standard demo size)
- **WHEN** any of the 9 mock-up forms renders
- **THEN** the rendered DOM + computed styles are byte-identical to pre-change

#### Scenario: Phone form usable

- **GIVEN** viewport at 375x667
- **WHEN** LeaveForm renders
- **THEN** fields stack single-column; buttons reachable without horizontal scroll

### Requirement: Touch targets ≥ 44 pixels

All interactive elements (buttons, links, dropdowns, list items) SHALL have minimum tap-target size of 44x44 px on phone viewports per Apple HIG / Google Material guidance.

#### Scenario: Buttons hit-target conformant

- **WHEN** any button renders on phone
- **THEN** its bounding box is ≥ 44 px in both dimensions
