# Design notes

## 1. Why touch the 9 mock-up forms (vs leaving them desktop-only)

Forms are *the* user-facing surface for everyday use. If forms don't work on phone, the whole "approve on the go" promise fails. The trade-off:

- Desktop demo at large window: visuals byte-identical (responsive classes activate only below 1024 px)
- Phone access: now functional

This is the one carve-out from the "don't touch demo screens" rule. Strictly Tailwind class additions; no JSX restructure; no logic change. A demo at the standard 1280x720 window sees zero change.

## 2. Sidebar → drawer transition

Phone (< 640 px):
- Top bar: logo + hamburger + Bell + RoleSwitcher (compact)
- Hamburger opens a slide-in drawer (left side)

Tablet (640-1024):
- Narrow sidebar (60 px) showing icons only
- Hover/tap expands

Desktop (≥ 1024):
- Full sidebar as today

Implementation: CSS-driven via Tailwind's `lg:`, `md:`, `sm:` prefixes. No JS transition library; CSS transitions cover it.

## 3. Repeater on phone

Desktop: `<table>` with column-per-subfield.

Phone: each row becomes a card stacking sub-fields:

```
┌─ Item 1 ─────────────┐
│ Category: 餐費       │
│ Amount: 350          │
│ Description: 客戶餐敘 │
│ [Remove]             │
└──────────────────────┘
```

Implementation: same `<RepeaterField>` component; conditional rendering via `useBreakpoint()` hook (or pure-CSS via grid + display).

## 4. Approval / Reject buttons on phone

Sticky bottom bar showing primary actions; main content scrolls behind. Avoids scroll-to-find-button friction.

## 5. Charts responsive

`recharts` `<ResponsiveContainer width="100%" height={300}>` already handles resize. Verify each chart wrapped this way.

## 6. Hit targets

Tailwind's default button padding gives 36-40 px on small. Add `min-h-[44px]` for touch-mode buttons (use a media query or `touch:` variant if available; else apply to all buttons unconditionally — slight desktop visual change but net usability win).

## 7. Drag-and-drop on touch

The DepartmentsTree uses react-arborist DnD. On touch devices: replace with explicit "Move to..." button + dialog showing the parent picker. react-arborist's touch DnD is mediocre; explicit UI is cleaner.

## 8. Open questions

- **Form focus management**: on phone, virtual keyboard pops; ensure inputs scroll into view (`scrollIntoViewIfNeeded`).
- **Wizard StepForms preview**: preview dialog on phone uses full screen; provide close button prominently.
- **Calendar year-grid on phone**: 12-month grid is too dense. Replace with monthly carousel + "go to today" button.
- **Print styles**: not in scope; PDF export covers print.
