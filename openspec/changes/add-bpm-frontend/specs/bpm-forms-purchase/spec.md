## ADDED Requirements

### Requirement: HWP Hardware Purchase form

The system SHALL provide an `HWPForm` screen with steps `[APPLY, IT SPEC REVIEW, QUOTATION, CONFIRM & PRINT, APPROVE, PO PROCESSING, CLOSE]`. The form SHALL include a Hardware Purchase repeater (Category, Item, Spec, Qty, Add/Copy/Delete actions) and a Request Information block (Requestor, Date, Request No., Shipping Location, Charge to, Project, Expected Date, Purpose, Note). The form SHALL include an Attachment drop zone.

#### Scenario: Add an item row
- **WHEN** the user clicks Add on a hardware row
- **THEN** a new row appears beneath it with empty fields and a row number incremented

### Requirement: ITPR IT Purchase Request (read-only)

The system SHALL provide an `ITPRView` screen showing a closed IT Purchase Request: header (Requestor, Request Date, Request No., Shipping Location, Charge to, Project, Expected Date, Purpose, PR Status), Software Purchase repeater with computed totals (subtotal + VAT + Total), a Status pill, and a History Log table with at least 5 entries spanning Submit / Approve / Confirm / Approve / Closed actions.

#### Scenario: Closed-state pipeline
- **WHEN** the user opens ITPR
- **THEN** the stepper shows all 7 steps with the last (`CLOSE`) highlighted as `current` and the prior 6 with green check marks
