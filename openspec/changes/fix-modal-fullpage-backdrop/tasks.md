# Tasks

## 1. Modal primitive

- [ ] 1.1 Create `bpm-ui/src/components/ui/Modal.tsx` with portal + backdrop + body-scroll lock + focus trap + Escape + click-out
- [ ] 1.2 Add `useModalStack` to track nested modals + drive z-index
- [ ] 1.3 Add z-index tokens (`--z-modal`, `--z-modal-stacked`) to shared CSS
- [ ] 1.4 Manual visual check: open a modal from inside a long-scrolling page; backdrop covers full viewport

## 2. Refactor existing modals

- [ ] 2.1 Rewrite `ConfirmDialog.tsx` over `<Modal>` — keep existing props
- [ ] 2.2 Rewrite `BpmnView.tsx` over `<Modal>` — keep existing props
- [ ] 2.3 Rewrite `ImpersonationModal.tsx` over `<Modal>` — keep existing props
- [ ] 2.4 Delete duplicate `components/ui/confirm-dialog.tsx`; redirect imports
- [ ] 2.5 Grep for `fixed inset-0.*bg-black` to catch any other hand-rolled overlays

## 3. Verify

- [ ] 3.1 `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 3.2 Boot bpm-ui dev server; open a form, click "View BPMN" → backdrop covers entire viewport
- [ ] 3.3 Submit a leave → ConfirmDialog backdrop covers entire viewport
- [ ] 3.4 Stack a confirm on top of impersonation modal → top modal Escape closes only top
- [ ] 3.5 chrome-devtools screenshot of each modal (fullPage=true) attached to the PR
