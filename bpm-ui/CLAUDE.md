# bpm-ui — employee app notes

Project-wide conventions live in the root `CLAUDE.md`. Backend conventions
live in `../bpm-svc/CLAUDE.md`. This file covers the employee SPA after
the all-flows-real Phase 1 (PR-L1 → PR-L6).

## Form mode contract

Every flow form (`screens/forms/*.tsx`, 11 components) implements the
same dual-mode props:

```ts
type FormProps = {
  persona: PersonaCode
  mode?: 'create' | 'task' | undefined  // default 'create'
  taskId?: string | null                 // required when mode === 'task'
  onSubmitted?: () => void               // called after successful submit
}
```

- `mode === 'create'` — employee starts a new instance. Form submits
  `POST /api/processes` and routes back to Home with a toast.
- `mode === 'task'` — assignee handles an inbox task. Form fetches
  `GET /api/tasks/{taskId}` for the field snapshot, renders the
  appropriate node panel (multi-step forms switch panel by
  `task.task.nodeId`), and exposes Approve / Reject / Return buttons
  via FormShell ActionBar. Submit routes through
  `POST /api/tasks/{id}/submit` (or `/return`).
- Both modes share the same React component — no separate "view" file.

The 4 originally read-only views (`EXTOBView`, `ITPRView`, `TEOView`,
`TRQView`) were promoted to full dual-mode forms in PR-L2 but kept their
`*View.tsx` filename to avoid an import shuffle. Renaming to `*Form.tsx`
is a clean-up follow-up.

## Hooks

Three runtime hooks back the form mode contract:

- **`useFlowSubmit(specCode)`** — wraps `POST /api/processes`. Returns
  `{ submit(formData), pending, error }` and resolves to
  `{ instanceId, firstTaskId }`.
- **`useFlowTask(taskId)`** — loads `GET /api/tasks/{id}` and exposes
  `submit / return / claim` actions, polling and refresh.
- **`useFormRuntime({ specCode, mode, taskId, onSubmitted })`**
  (`.tsx`) — the wrapper every form actually uses. Combines the two
  above + a built-in `<FlowToast />`, success/error handling, and
  `onSubmitted` callback. Each form imports this single hook regardless
  of mode.

Inbox listing uses `useMyTasks(status)` (`open` | `completed` | `all`)
and `useMyInstances(status)`. Both poll every 30s and expose a manual
`refresh()`. The DTOs include `specCode` so a row click can route into
the correct form without an extra fetch.

## Inbox routing via App.tsx Screen union

`Screen` (in `components/AppLayout.tsx`) is a discriminated union with a
`'form'` variant carrying `code: FormCode` and `taskId?: string`:

```ts
{ kind: 'form'; code: FormCode; taskId?: string }
```

`App.tsx` switches on `screen.kind` and, for `'form'`, picks the form
component by `screen.code`. The presence of `taskId` automatically flips
`mode` to `'task'`:

```ts
const formMode = screen.taskId ? 'task' : 'create'
```

`Home.tsx` and `Search.tsx` both build the screen value from a task or
instance row — `setScreen({ kind: 'form', code: task.specCode, taskId: task.id })`.
No separate "Inbox" screen exists; the inbox is the Pending Action table
on Home.

## Demo guard — Phase 1 status

After PR-L1..L6 the following files are explicitly **unlocked** (live code,
not demo guard):

- `src/screens/forms/` — all 11 form components
- `src/screens/Home.tsx`
- `src/screens/Search.tsx`
- `src/lib/workflow.ts` — kept, but the `FORMS` map is now a label /
  display config (中文名 / step labels / persona icons). Flow shape lives
  in `sample_specs/*.json`. Phase 2 form-runtime-rendering can deprecate
  this map entirely.

Still under demo guard:

- `src/screens/Report.tsx` — waiting on `add-real-reporting`. The Home
  `Activity Feed` and `Reminders` widgets are also still backed by
  `MOCK_ACTIVITY` / `MOCK_REMINDERS` from `lib/mocks.ts` (small `demo`
  tag in the corner).

`MOCK_CASES` and friends in `lib/mocks.ts` are not deleted — Attendance
and a few legacy screenshots still import them. Removal is a separate
clean-up PR.

## Type-check

`tsc -p tsconfig.app.json --noEmit` is the canonical type-check. Without
`-p tsconfig.app.json` it silently skips files under `src/`. There is no
JS test framework wired (no vitest / jest); rely on tsc + manual boot
(`npm run dev`) + chrome-devtools screenshots (default `fullPage=true`).

## Phase 2 entry

`add-form-runtime-rendering` (openspec change) will replace the 11
hand-coded form components with a single `<DynamicForm spec={...}
mode="..." />` reading `userTask.fields[]` from the spec snapshot.
Once it lands, the FORMS label map can be deprecated and the
`*View.tsx` rename becomes moot.
