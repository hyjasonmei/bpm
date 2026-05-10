# Tasks

## 1. Backend — Domain

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/HrFlows/HrFlowSpecCode.cs` enum (`Resign`, `Deptx`)
- [ ] 1.2 Create `HrFlowStatus.cs` enum (`PendingManager`, `PendingHr`, `Returned`, `Completed`, `Cancelled`)
- [ ] 1.3 Create `HrFlowStep.cs` enum (`Apply`, `ManagerApprove`, `HrApprove`, `Closed`)
- [ ] 1.4 Create `HrFlowActionType.cs` enum (`Submit`, `Approve`, `Return`, `Cancel`)
- [ ] 1.5 Create `HrFlowInstance.cs` (inherits AuditableEntity)
- [ ] 1.6 Create `HrFlowAction.cs` (append-only audit row)

## 2. Backend — Persistence

- [ ] 2.1 Create `Persistence/Configurations/HrFlows/HrFlowInstanceConfiguration.cs`
- [ ] 2.2 Create `HrFlowActionConfiguration.cs`
- [ ] 2.3 Indexes on `HrFlowInstance`: `(TenantId, InitiatorUserId, LastActivityAt DESC)`, `(TenantId, Status)`, `(TenantId, ResolvedManagerUserId, Status)`
- [ ] 2.4 Index on `HrFlowAction`: `(InstanceId, CreatedAt)`
- [ ] 2.5 Block UPDATE/DELETE on `HrFlowAction` via interceptor (or note: defer to runtime later)
- [ ] 2.6 Add DbSets to `BpmDbContext`
- [ ] 2.7 `dotnet ef migrations add AddHrFlows`
- [ ] 2.8 Apply locally; verify schema

## 3. Backend — Application service

- [ ] 3.1 DTOs: `HrFlowInstanceDto`, `HrFlowActionDto`, `StartHrFlowRequest`, `ApproveRequest`, `ReturnRequest`, `ResubmitRequest`
- [ ] 3.2 `IHrFlowService` interface with 8 methods (per proposal)
- [ ] 3.3 `HrFlowService.cs` — `StartAsync`: lookup initiator's manager, fail if none; create instance with Status=PendingManager
- [ ] 3.4 `ApproveAsync`: branch on current Status; PendingManager → PendingHr (verify caller = ResolvedManagerUserId); PendingHr → Completed (verify caller has hr role)
- [ ] 3.5 `ReturnAsync`: only when Status=PendingManager; verify caller = ResolvedManagerUserId; require comment; set Status=Returned, Step=Apply
- [ ] 3.6 `ResubmitAsync`: only when Status=Returned; verify caller = InitiatorUserId; replace FormDataJson; set Status=PendingManager, Step=ManagerApprove
- [ ] 3.7 `CancelAsync`: only when Status != Completed; verify caller = InitiatorUserId
- [ ] 3.8 `GetMineAsync` / `GetMyTodoAsync` / `GetByIdAsync` (with permission check: initiator | resolved manager | any HR if PendingHr)
- [ ] 3.9 Every state transition writes a `HrFlowAction` row in same transaction
- [ ] 3.10 Register in `Application/DependencyInjection.cs`

## 4. Backend — API

- [ ] 4.1 `Api/Controllers/HrFlowsController.cs`
- [ ] 4.2 `POST /api/hr-flows/{specCode}` (specCode bound to HrFlowSpecCode enum, validate Resign/Deptx)
- [ ] 4.3 `GET /api/hr-flows/mine`, `/todo`, `/{id}`
- [ ] 4.4 `POST /api/hr-flows/{id}/approve`, `/return`, `/resubmit`, `/cancel`
- [ ] 4.5 `[Authorize]` on all; UserId derived from auth context
- [ ] 4.6 Map application exceptions to 403 / 409 / 404 cleanly

## 5. Backend — Tests

- [ ] 5.1 Unit: StartAsync without manager → throws
- [ ] 5.2 Unit: ApproveAsync as wrong actor → throws ForbiddenException
- [ ] 5.3 Unit: ReturnAsync at PendingHr step → throws InvalidOperationException
- [ ] 5.4 Unit: ResubmitAsync as non-initiator → throws
- [ ] 5.5 Integration: full happy path RESIGN — initiator start → manager approve → hr approve → Status=Completed
- [ ] 5.6 Integration: return path — start → manager return → resubmit → manager approve → hr approve → Completed; assert 5 HrFlowAction rows in correct order
- [ ] 5.7 Integration: two HR users approve simultaneously — second one fails cleanly (status no longer PendingHr)
- [ ] 5.8 Integration: cancel after Completed → 409

## 6. Frontend — workflow + nav

- [ ] 6.1 `bpm-ui/src/lib/workflow.ts`: extend `FormCode` union with `RESIGN | DEPTX`
- [ ] 6.2 Add `RESIGN` and `DEPTX` entries to `FORMS` Record (3 steps each, ownerByStep `['employee', 'manager', 'hr', null]`, initialActive 0)
- [ ] 6.3 `bpm-ui/src/components/AppLayout.tsx`: extend HR group in `FORM_GROUPS` with two items
- [ ] 6.4 Verify Create dropdown shows both new items under HR

## 7. Frontend — API client + types

- [ ] 7.1 `bpm-ui/src/types/hrFlows.ts`: mirror backend DTOs
- [ ] 7.2 `bpm-ui/src/lib/api/hrFlows.ts`: 8 functions matching API endpoints
- [ ] 7.3 Auth header / base URL via existing api convention

## 8. Frontend — RESIGN form

- [ ] 8.1 `bpm-ui/src/screens/forms/ResignForm.tsx`
- [ ] 8.2 4 fields: expectedLastDay (date), reason (textarea), handover (text), note (textarea)
- [ ] 8.3 Use existing `FormShell` for header / stepper
- [ ] 8.4 Approval panel: ManagerApprove step → Approve + Return buttons; HrApprove step → Approve only
- [ ] 8.5 On submit → call `start('RESIGN', formData)`
- [ ] 8.6 On approve / return / resubmit / cancel → call respective api, refetch instance

## 9. Frontend — DEPTX form

- [ ] 9.1 `bpm-ui/src/screens/forms/DeptxForm.tsx`
- [ ] 9.2 4 fields: currentDepartment (read-only auto-fill), targetDepartment (select), effectiveDate (date), reason (textarea)
- [ ] 9.3 Same FormShell + approval panel pattern as ResignForm
- [ ] 9.4 Department select source: existing org/department endpoint (or hardcode list if endpoint not ready — note in code)

## 10. Frontend — wire into routing

- [ ] 10.1 In `App.tsx` (or wherever form router lives), add cases for `screen.kind === 'form' && screen.code === 'RESIGN'` → render `<ResignForm/>`; same for `DEPTX`

## 11. Verify

- [ ] 11.1 `cd bpm-ui && npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 11.2 `cd bpm-ui && npm run build` clean
- [ ] 11.3 Backend `dotnet build` clean
- [ ] 11.4 Backend `dotnet test` green
- [ ] 11.5 E2E manual: start RESIGN as employee → switch to manager persona → see in todo → return with comment → switch back to employee → see Returned → resubmit → manager approve → switch to HR → approve → status = Completed
- [ ] 11.6 E2E manual: same for DEPTX
- [ ] 11.7 Browser screenshot via chrome-devtools (fullPage) for both forms — save to `dogfood-screenshots/`
