## ADDED Requirements

### Requirement: Report dashboard

The system SHALL provide a `Report` screen with three views over the mock dataset: (1) **Counts by Form Type** as a horizontal bar chart, (2) **Counts by Status** as a stacked bar chart, (3) **Monthly Volume** as a sparkline-style line chart over the last 6 months. Each view SHALL render with the same brand palette (slate / amber / blue / green) without pulling a charting library — CSS-only bars are acceptable for the demo.

#### Scenario: Counts by Form Type
- **WHEN** the user opens Report
- **THEN** a horizontal bar chart shows all 9 form types ordered by count descending, each bar labeled with the count and width proportional to the max

#### Scenario: Counts by Status
- **WHEN** the user opens Report
- **THEN** a stacked bar shows the 7 statuses (Draft / Pending Approval / Approved / FIN Review / IT Spec Review / Returned / Closed) in fixed brand colors

#### Scenario: Monthly Volume
- **WHEN** the user opens Report
- **THEN** a small line chart shows the last 6 months' submission counts derived from `MOCK_CASES.submitted` dates
