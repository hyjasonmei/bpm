// Backward-compatible re-export. The canonical implementation lives in
// ./ConfirmDialog (PascalCase) and sits on the shared `<Modal>` primitive
// for full-viewport backdrop. Callers may import from either path; new
// code SHOULD prefer the PascalCase path.
export { ConfirmDialog } from './ConfirmDialog'
export type { ConfirmDialogProps } from './ConfirmDialog'
