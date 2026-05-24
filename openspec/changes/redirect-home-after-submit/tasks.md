# Tasks

## 1. Invalidation event

- [ ] 1.1 Dispatch `bpm:tasks-invalidate` from `useFormRuntime` after every successful submitCreate / submitUserTask / approve / reject / returnTask
- [ ] 1.2 `useMyTasks` + `useMyInstances` register a window listener and call `refresh()` on receipt
- [ ] 1.3 De-bounce: if multiple events fire within 250 ms, coalesce to one refresh

## 2. Redirect policy

- [ ] 2.1 After Create-mode submit, route to `/tasks/:firstTaskId` when the first task's assignee is the current user, else `/`
- [ ] 2.2 After Task-mode submit / approve / reject / return, route to `/`
- [ ] 2.3 Keep `onSubmitted` as the override hook for callers that need a different destination
- [ ] 2.4 Surface a toast on Home identifying the just-submitted instance ("Submitted • LEAVE #ab12 → 待主管簽核")

## 3. Verify

- [ ] 3.1 `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 3.2 Manual: submit LEAVE as employee → lands on Home → new instance is in inbox **without waiting 30 s**
- [ ] 3.3 Manual: approve a task → lands on Home → task disappears immediately
- [ ] 3.4 Manual: a spec whose first task is self-assigned routes to `/tasks/:id`, not `/`
- [ ] 3.5 chrome-devtools screenshot of Home immediately after a submit, showing the new row + toast
