## ADDED Requirements

### Requirement: GEE Employee Expense form

The system SHALL provide a `GEEForm` screen with steps `[APPLY, APPROVE, CONFIRM & PRINT, FIN REVIEW, CLOSE]`. The form SHALL include an info row (Requestor, Cost Center, Business Unit), a yellow info banner stating "Expense reimbursement application shall be submitted within one month after spending (based on invoice date). 所有費用依規定需在憑證(發票或收據)所載日期1個月內完成費用申請，逾期依規定將退回不予受理。", and a repeating Invoice block per item with: Invoice Date, Invoice No., Charge to, Project, Category, Amount (with currency selector), Description, optional Recharge-to-outside-Taiwan checkbox, and a per-row Add / Copy / Delete action column.

#### Scenario: Add a second invoice row
- **WHEN** the user clicks "Add" on the rightmost action column
- **THEN** a new invoice block appears beneath the first with empty fields, numbered Invoice #2

#### Scenario: Total bar at the bottom
- **WHEN** any invoice row has an Amount filled
- **THEN** the bottom of the form shows `Total: NTD <sum> (USD <converted>)` updating live as the user types

### Requirement: GEV Vendor Expense form

The system SHALL provide a `GEVForm` screen with the same step list as GEE plus a Vendor block (vendor selector + "New Vendor" toggle), Payment Term selector, "Payment based on contract" reference field, and a per-invoice VAT rate selector (0% / 5% / 10%) showing computed Subtotal + VAT + Invoice Total.

#### Scenario: VAT computed
- **WHEN** invoice subtotal is NTD 333 and VAT rate is 5%
- **THEN** the form shows `Subtotal NTD 333`, `VAT NTD 17`, `Invoice Total NTD 350`

### Requirement: APE Advance Payment form

The system SHALL provide an `APEForm` screen with the same step list. Fields: "The date you expect to receive the cash" + "The date you will deduct / return the advance" (both required dates), Charge Department, Description, Amount + currency, Attachment, Note. The form SHALL show `Total: NTD <amount>` at the bottom.

#### Scenario: Total reflects entered amount
- **WHEN** the user enters Amount `5000` with currency NTD
- **THEN** the bottom Total bar shows `NTD 5,000 (USD 165.62)` using the same NTD→USD rate the prototype uses
