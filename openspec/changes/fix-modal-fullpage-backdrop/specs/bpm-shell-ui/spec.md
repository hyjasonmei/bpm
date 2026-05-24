# bpm-shell-ui (delta) — Modal primitive

## ADDED Requirements

### Requirement: Modal primitive

The shell SHALL expose a single `<Modal>` component in
`bpm-ui/src/components/ui/Modal.tsx`. All overlay dialogs in bpm-ui MUST
be built on top of it.

#### Scenario: Backdrop covers the full viewport regardless of mount point

- **WHEN** a `<Modal open>` is rendered inside an ancestor that has
  `overflow: hidden`, `transform`, `filter`, or `contain: paint`
- **THEN** the backdrop SHALL still cover the entire viewport
- **AND** content underneath SHALL NOT be visible through the backdrop

Implementation note: rendered via React Portal into `document.body`.

#### Scenario: Body scroll is locked while a modal is open

- **WHEN** any `<Modal>` becomes open
- **THEN** `html, body` SHALL have `overflow: hidden`
- **AND** scrolling inside the modal SHALL still work
- **WHEN** the last open `<Modal>` closes
- **THEN** body scroll SHALL be restored

#### Scenario: Escape and backdrop click close the topmost modal

- **WHEN** Escape is pressed
- **THEN** only the topmost open modal SHALL close
- **WHEN** the backdrop is clicked
- **THEN** the modal SHALL close UNLESS the caller passed
  `dismissOnBackdrop={false}` (for destructive confirms)

#### Scenario: Focus management

- **WHEN** a modal opens
- **THEN** focus SHALL move into the modal
- **AND** Tab cycling SHALL stay inside the modal
- **WHEN** a modal closes
- **THEN** focus SHALL return to the element that opened it

### Requirement: Z-index tokens

CSS tokens `--z-modal: 60` and `--z-modal-stacked: 70` SHALL be defined
in the shared token sheet so modal layering is not hard-coded inside
component classes.

### Requirement: Existing dialogs wrap the primitive

`ConfirmDialog`, `BpmnView`, `ImpersonationModal`, and every future
overlay SHALL be implemented as a wrapper over `<Modal>`.

The duplicate `components/ui/confirm-dialog.tsx` SHALL be removed; the
canonical export is `components/ui/ConfirmDialog.tsx`.

#### Scenario: Caller contract preserved

- **WHEN** a feature form imports `<ConfirmDialog>` with its existing
  prop shape (`open`, `title`, `description`, `onConfirm`, `onCancel`,
  …)
- **THEN** the dialog SHALL behave the same as before from the caller's
  perspective
