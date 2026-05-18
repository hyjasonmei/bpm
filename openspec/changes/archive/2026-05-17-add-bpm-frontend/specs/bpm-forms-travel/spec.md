## ADDED Requirements

### Requirement: TRQ Travel Request (read-only)

The system SHALL provide a `TRQView` screen showing a closed travel request with header (Requestor, Cost Center, Business Unit, Request Date, Request No., Status), Itinerary block (Travel Type, Departure City, Destination City, Depart Date, Return Date, Charge to, Project, Travel Purpose), Travel Reservation block (Flight Request, Passport ID, Passport Name, Passport Expire, Frequent Flyer No., Special Food Request, Mobile Number, Travel Agent, optional Pickup checkboxes), Deducted History (per-diem and previous-trip references), Attachment list, History Log, and an Expected Approvers panel on the right.

#### Scenario: Travel reservation block
- **WHEN** the user opens TRQ
- **THEN** the Travel Reservation card is visible with at minimum the Passport ID, Passport Name, Passport Expire, and Mobile Number fields populated

### Requirement: TEO Travel Expense (read-only)

The system SHALL provide a `TEOView` screen for closed travel-expense reports: header info, an Original Travel Request Plan link/summary, a Travel Expense repeater (Date, Country, Description, Category, Amount with currency converter), a Per-diem Calculation block (per destination, breakfast / meals / deductions), an Advance Payment for Deduction reference, a Net Amount summary, attachments, History Log, and Expected Approvers.

#### Scenario: Net Amount renders after deduction
- **WHEN** the user opens TEO and the original case had an advance of NTD 50,000 against a total expense of NTD 97,249
- **THEN** the Net Amount summary shows the total in red plus the deduction line and the resulting net payable in red
