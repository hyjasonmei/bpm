## ADDED Requirements

### Requirement: Home dashboard layout

The system SHALL provide a `Home` screen as the default landing page. The layout SHALL be a two-column grid: the left column (stretched) holds the Pending My Action table and the My Cases table; the right column (fixed ~280px) holds the Quick Actions panel, the Activity Feed, and the Reminders panel. Above both columns SHALL be a greeting line + four stat cards.

#### Scenario: Greeting reflects persona
- **WHEN** the active persona is Employee
- **THEN** the greeting reads `👋 Good morning, Wilson` and the sub-line reads `You have N cases pending your action today.`

#### Scenario: Greeting flips to Manager view
- **WHEN** the active persona is Manager
- **THEN** the greeting reads `👋 Good morning, Wilson — Manager View` and the sub-line counts the cases pending the manager's approval

### Requirement: Stat cards

The Home page SHALL render four stat cards above the main grid. Card content SHALL change based on persona:

- Employee view: `My Pending Actions`, `My Drafts`, `My Approved (30d)`, `My Total Cases`
- Manager view: `Pending My Approval`, `Approved Today`, `Returned`, `Team Total Cases`
- Other personas (finance / it / hr / admin): each shows the queues relevant to that role; admin shows system-wide totals.

#### Scenario: Stat cards update on persona switch
- **WHEN** the user switches from Employee to Manager
- **THEN** the four stat-card titles and counts change to the manager set listed above

### Requirement: Pending My Action table

Each Home view SHALL render a sortable table of cases pending the current persona's action, columns: Request No., Type, Type Label, Requestor (manager view) / Type Label only (employee view), Submitted, Days outstanding, Action, and a primary "Open" button to jump into the form.

#### Scenario: Days-outstanding badge
- **WHEN** a row's "Days outstanding" value exceeds 7
- **THEN** the value renders with an amber badge to highlight aging cases

### Requirement: Activity Feed and Reminders

The Home right rail SHALL include an Activity Feed (recent approvals / returns / submissions, max 6 entries) and a Reminders panel (drafts to finish, upcoming travel, contract expirations, advance return due dates). Both SHALL be read-only and color-coded by event type.

#### Scenario: Activity Feed format
- **WHEN** an activity is "approved by Elton Yang on TW-GEE-26-001342, 2 hours ago"
- **THEN** the row shows a green check icon, the actor + verb, the document number as a monospace chip, and the relative time on the right
