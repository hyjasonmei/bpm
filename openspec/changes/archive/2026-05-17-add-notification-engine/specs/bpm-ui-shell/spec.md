## ADDED Requirements

### Requirement: Bell icon shows unread count and toggles dropdown

The `AppLayout` Bell button SHALL be interactive: clicking it SHALL toggle a `NotificationBellDropdown` panel anchored below the icon. The button SHALL display an unread-count badge when the user has at least one unread `in_app` `NotificationDelivery`. The badge SHALL update via polling every 60 seconds while the app is open, and SHALL update immediately after the user marks a delivery read.

#### Scenario: Badge appears with unread count

- **GIVEN** the current user has 3 unread in-app deliveries
- **WHEN** the AppLayout mounts and polls the inbox
- **THEN** the Bell badge shows `3`

#### Scenario: Badge clears after marking all read

- **GIVEN** badge shows `3`
- **WHEN** the user marks all 3 read
- **THEN** the badge disappears (no badge for 0)

#### Scenario: Click toggles dropdown

- **WHEN** the user clicks the Bell icon
- **THEN** the dropdown opens; clicking again closes it; clicking outside also closes it

### Requirement: NotificationBellDropdown lists newest 20 unread

The dropdown SHALL render the user's newest 20 unread `in_app` deliveries (newest first), each showing:

- Subject text (truncated to one line, ~60 chars)
- Relative timestamp (e.g., "2 分鐘前", "3 小時前")
- A "✓" icon button that marks the delivery read

The dropdown SHALL include a "View all →" footer link routing to the `/notifications` screen for the full inbox + history.

When the user has zero unread deliveries, the dropdown SHALL show "目前沒有未讀通知" rather than an empty list.

#### Scenario: Newest 20 shown

- **GIVEN** the user has 50 unread deliveries
- **WHEN** the dropdown opens
- **THEN** only the 20 newest are listed; "View all →" link points to /notifications

#### Scenario: Empty state message

- **GIVEN** the user has zero unread deliveries
- **WHEN** the dropdown opens
- **THEN** the panel shows "目前沒有未讀通知"

#### Scenario: Mark-read in-place removes the row

- **WHEN** the user clicks the ✓ on a row
- **THEN** the row disappears from the dropdown immediately (optimistic update); the badge count decrements; the API call updates the server in the background

### Requirement: Notifications screen lists full inbox with filters

A new `/notifications` route SHALL render the `Notifications` screen showing the full inbox (read + unread, plus dismissed) with filtering by date range, trigger, and status. The screen SHALL allow bulk mark-read and bulk mark-dismissed.

#### Scenario: Full inbox listing

- **WHEN** the user navigates to /notifications
- **THEN** all of their `in_app` deliveries are listed with status indicators (unread / read / dismissed); pagination at 50 rows per page

#### Scenario: Filter by trigger

- **WHEN** the user selects trigger filter `on_assign`
- **THEN** only `on_assign` deliveries are shown

### Requirement: Polling lifecycle

Polling SHALL run while at least one `useNotificationPolling` hook is mounted (typically the AppLayout Bell). Polling SHALL pause when the browser tab is hidden (`document.visibilityState = 'hidden'`) and resume when visible. Polling SHALL also trigger an immediate refresh after the user explicitly performs a mark-read or mark-dismissed action.

#### Scenario: Polls every 60s when visible

- **GIVEN** AppLayout is mounted and tab is visible
- **WHEN** 60 seconds elapse
- **THEN** `GET /api/notifications/inbox?unread=true` is called

#### Scenario: Polling pauses on hidden tab

- **WHEN** the user switches to another tab (visibilitychange = hidden)
- **THEN** no further polls fire until the tab becomes visible again

### Requirement: Demo screens preserved

The mock-up flow screens (`bpm-ui/src/screens/forms/*.tsx`, `Home.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts`) SHALL NOT be modified by this change. The Bell icon's visual appearance is unchanged when the user has zero unread deliveries (no badge); only the click behavior and the badge-when-unread are added.

#### Scenario: Demo screens visually unchanged at zero unread

- **GIVEN** a demo persona with zero unread deliveries
- **WHEN** the AppLayout renders
- **THEN** the Bell icon looks identical to the pre-change state — no badge, no dropdown until clicked

#### Scenario: Mock-up forms unchanged

- **WHEN** the change is applied
- **AND** a reviewer opens any of the 9 mock-up flows
- **THEN** the form visuals are byte-identical to pre-change
