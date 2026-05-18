## ADDED Requirements

### Requirement: Leave Request form

The system SHALL provide a `LeaveForm` screen for the new LEAVE workflow with steps `[APPLY, MANAGER APPROVE, HR RECORD, CLOSED]`. The form SHALL collect: requestor (auto-filled from active persona), department (auto-filled), leave type (Annual / Sick / Personal / Marriage / Bereavement / Maternity / Paternity / Other), start date, end date, computed total days (counting working days excluding weekends), reason text area, optional attachment, and proxy / delegated approver during the leave.

#### Scenario: Leave type dropdown
- **WHEN** the user opens the leave-type select
- **THEN** the eight options listed above appear, each shown bilingually (e.g. `Annual / 特休`)

#### Scenario: Total days computed
- **WHEN** the user picks start `2026-05-04` and end `2026-05-08`
- **THEN** the form shows `Total: 5 days (M T W T F)` derived client-side, excluding the bracketing weekend

#### Scenario: Negative range rejected
- **WHEN** the user picks start later than end
- **THEN** the days calculator displays `Invalid range` and the Submit button is disabled

#### Scenario: Manager approval surface
- **WHEN** the active persona is Manager and the leave is at MANAGER APPROVE
- **THEN** the form bottom shows "Reject" (with a comment textarea) and "Approve" buttons

#### Scenario: HR record surface
- **WHEN** the active persona is HR and the leave is at HR RECORD
- **THEN** the form bottom shows a "Record & Close" primary button, plus an HR-only field for "Annual leave balance" displayed read-only from a mock balance table

### Requirement: External Employee Onboarding (read-only)

The system SHALL provide an `EXTOBView` screen for the EXTOB workflow with steps `[SUBMIT, CREATING ACCOUNT, CLOSED]`. This screen SHALL be read-only and represent a closed/completed onboarding case, including header info (Hiring Manager, Business Title, Employee Location, Request No.), New Hire Info, Contract Info, Tasks table (with Complete checkmarks), and a History Log.

#### Scenario: Onboarding renders the closed state by default
- **WHEN** the user opens EXTOB
- **THEN** the stepper shows all three steps with the third highlighted as `current` and the first two with green checks (matching the prototype's `activeStep:2`)
