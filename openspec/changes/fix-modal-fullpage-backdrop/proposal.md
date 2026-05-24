## Why

Jason reported 2026-05-24 that every modal in bpm-ui shows a broken
backdrop — the dark overlay doesn't actually cover the whole viewport,
so the page underneath bleeds through and the dialog looks unfinished.
This affects:

- `ConfirmDialog` (submit / approve / reject confirms)
- `BpmnView` (the "View BPMN" modal on every form)
- `ImpersonationModal`
- any future modal — there is no shared base today

Root cause: each modal hand-rolls its `fixed inset-0` + z-index +
backdrop blur, and some of them sit inside a scroll container whose
`overflow: hidden` (or transform / filter) creates a new stacking
context that clips the `fixed` overlay back to the parent box instead of
the viewport. The two parallel files
(`components/ui/ConfirmDialog.tsx` and `components/ui/confirm-dialog.tsx`)
also diverge in styling, which is its own smell.

## What Changes

### NEW `components/ui/Modal.tsx`

Single low-level primitive every modal sits on top of. Responsibilities:

- Renders into `document.body` via React Portal so no ancestor's
  `overflow` / `transform` / `filter` can clip the backdrop.
- Backdrop is `fixed inset-0 z-[var(--z-modal)]` with `bg-black/50`,
  full-viewport regardless of where the caller mounted it.
- Body-scroll lock while any modal is open (toggle a `body[data-modal-open]`
  attribute; CSS sets `overflow: hidden` on `html, body`).
- Escape key closes; click-on-backdrop closes (caller can opt out for
  destructive dialogs).
- Focus trap inside the dialog; focus restored to opener on close.
- Stacking: a tiny `useModalStack()` increments a counter so nested
  modals get incrementing z-index and only the top one responds to
  Escape.

### Refactor existing modals

`ConfirmDialog`, `BpmnView`, `ImpersonationModal`, and any other
hand-rolled overlay become **thin wrappers** over `<Modal>`. They keep
their existing prop contracts so chef-cooked feature code that calls
`<ConfirmDialog open={…} />` keeps working.

The duplicate `components/ui/confirm-dialog.tsx` (lowercase) is deleted
after callers are migrated to the canonical `ConfirmDialog.tsx`.

### Tokens

Add `--z-modal: 60` and `--z-modal-stacked: 70` to the existing CSS
token set so future modal-on-modal scenarios don't reach for magic z
numbers.

### Out of scope

- Animation polish (fade-in / scale-in) — separate follow-up.
- Drawer / sheet variants — separate change.
- Replacing `react-hot-toast` or other non-modal portals.

## Capabilities

### New

- `bpm-shell-ui` (UI core capability for bpm-ui shell components) — adds
  `Modal` primitive + full-viewport backdrop contract.

### Modified

- None (all caller behaviour preserved).

## Impact

- `bpm-ui/src/components/ui/Modal.tsx` — new
- `bpm-ui/src/components/ui/ConfirmDialog.tsx` — rewritten over Modal
- `bpm-ui/src/components/ui/confirm-dialog.tsx` — deleted (duplicate)
- `bpm-ui/src/components/BpmnView.tsx` — rewritten over Modal (also
  see `swap-form-bpmn-viewer-to-bpmnjs` for the diagram itself)
- `bpm-ui/src/components/ImpersonationModal.tsx` — rewritten over Modal
- `bpm-ui/src/styles/*.css` — z-index tokens
- No backend changes
- No migration
- No chef-skill update needed (the `<ConfirmDialog>` prop contract chef
  already uses doesn't change)
