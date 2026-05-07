## ADDED Requirements

### Requirement: RoleSwitcher dropdown shows active delegation

The `RoleSwitcher` dropdown (in `AppLayout`) SHALL render, above the persona picker section, a `DelegationSummary` block. The block content depends on the current user's delegations:

- **No active delegation**: shows `目前代理人：無` plus a `設定代理人 →` button
- **Active delegation**: shows `目前代理人：{delegate.full_name}（{startAt: M/d} - {endAt: M/d}）` plus a `管理代理人 →` button
- **Scheduled (future)**: shows `代理人：{delegate.full_name}（從 {startAt: M/d} 開始）` with a light-blue chip and `管理代理人 →` button

Clicking the button SHALL open the `DelegationManagementDialog` modal. The block SHALL use bilingual labels (zh-TW + en) following the existing RoleSwitcher i18n pattern.

#### Scenario: No delegation shown as 無

- **GIVEN** Wilson has no delegations
- **WHEN** Wilson opens the RoleSwitcher dropdown
- **THEN** the top section shows `目前代理人：無` + `[設定代理人 →]`

#### Scenario: Active delegation shown with window

- **GIVEN** Wilson has an active delegation to Yang, 5/10 - 5/15
- **WHEN** Wilson opens the RoleSwitcher dropdown
- **THEN** the top section shows `目前代理人：Yang（5/10 - 5/15）` + `[管理代理人 →]`

#### Scenario: Scheduled delegation surfaces upcoming context

- **GIVEN** Wilson has a scheduled delegation to Yang, starting in 7 days
- **WHEN** Wilson opens the RoleSwitcher dropdown
- **THEN** the section shows `代理人：Yang（從 5/13 開始）` with a light-blue chip and the management button

### Requirement: DelegationManagementDialog provides full self-service

The dialog SHALL display:

- **進行中** card — the active delegation if any; with delegate name, time range, days remaining counter, `Cancel` button, `Edit end time` button
- **預定中** list — scheduled (future) delegations; same actions plus a `Starts in N days` indicator
- **歷史** accordion (collapsed by default) — last 12 months of expired or cancelled rows for reference
- **新增代理人** form (always at bottom) — delegate user picker, start datetime, end datetime, optional reason; submit button disabled until valid

The form SHALL pre-validate:

- Delegate user picker excludes the current user (defense in depth — server also rejects)
- Start datetime defaults to tomorrow 00:00 in user's local timezone, minimum is current time
- End datetime defaults to start + 1 day; minimum is start + 1 hour
- Reason max 500 chars with character counter

On submit success, the dialog SHALL close, refresh the delegation context, and toast `代理人已設定` (or with cycle warning when present).

#### Scenario: Form submit creates and refreshes

- **GIVEN** Wilson opens the dialog and fills in valid delegate Yang, future window
- **WHEN** Wilson clicks `建立代理`
- **THEN** the API is called; on success the dialog closes; the RoleSwitcher block updates to show the new active or scheduled row; a toast appears

#### Scenario: Conflict shows inline error

- **GIVEN** Wilson submits a window overlapping an existing delegation
- **WHEN** the API returns 409
- **THEN** the form shows an inline error citing the conflicting row's window; submit remains disabled until the user adjusts

#### Scenario: Cycle warning toasted in yellow

- **GIVEN** Yang has a delegation pointing at Wilson; Wilson creates one pointing at Yang
- **WHEN** the API returns 201 with a cycle warning
- **THEN** the dialog closes and a yellow toast appears: `提醒：偵測到雙方互相代理`

### Requirement: InboundBanner appears on Home when current user is delegate

`bpm-ui/src/screens/Home.tsx` SHALL mount an `InboundBanner` component at the top. The banner reads from the delegation context's `inbound` list. When inbound is empty (the typical case for demo personas), the banner renders nothing — no visible change to Home. When inbound has at least one active row, the banner renders a slim notice strip describing the delegation:

- One inbound: `🔁 您目前代理 {granter.full_name}（{startAt: M/d} - {endAt: M/d}）— 期間内的任務會自動指派給您`
- Multiple: `🔁 您目前代理 {N} 位同事 — 點此展開` (clickable to expand a list)

The banner SHALL NOT modify any other content on Home.

#### Scenario: Banner hidden when no inbound

- **GIVEN** the current user has no inbound delegations
- **WHEN** Home renders
- **THEN** the InboundBanner returns null; Home looks identical to pre-change visually

#### Scenario: Banner shown for one inbound

- **GIVEN** the current user is the delegate of Wilson, active 5/10 - 5/15
- **WHEN** Home renders
- **THEN** the banner appears at the top: `🔁 您目前代理 Wilson（5/10 - 5/15）— 期間内的任務會自動指派給您`

### Requirement: Demo screens unmodified except Home banner

The change SHALL NOT modify `bpm-ui/src/screens/forms/*.tsx`, `Search.tsx`, `Report.tsx`, or `bpm-ui/src/lib/workflow.ts`. The only demo-screen modification permitted is mounting `<InboundBanner />` at the top of `Home.tsx` — which renders nothing when there is no inbound delegation, preserving the existing visual when no delegations are seeded.

For demo runs that absolutely require byte-identical visuals, the entire delegation UI section in `RoleSwitcher` SHALL be gated behind `import.meta.env.VITE_DELEGATION_ENABLED !== 'false'`. Setting `VITE_DELEGATION_ENABLED=false` in the relevant `.env` removes the dropdown section.

#### Scenario: Demo gate disables UI

- **GIVEN** `VITE_DELEGATION_ENABLED=false` in `.env`
- **WHEN** the app builds and runs
- **THEN** the RoleSwitcher dropdown shows no delegation section; InboundBanner returns null regardless of inbound state

#### Scenario: Form components untouched

- **WHEN** the change is applied
- **AND** a reviewer opens any of the 9 mock-up flows (LeaveForm / GEEForm / etc.)
- **THEN** the form visuals are byte-identical to pre-change
