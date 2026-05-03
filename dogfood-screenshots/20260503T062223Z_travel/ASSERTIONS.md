# Dogfood round 4 — TRAVEL flow + checklist v0.2 self-verify

Run: 2026-05-03T06:22:23Z
Branch: `dogfood-travel`
Spec: `sample_specs/travel_v1.json` (newly drafted in this round)
Test case: `tc_1` (domestic 高雄 8000)

This round has two purposes:
1. **Round 4 dogfood** of the spec→code pipeline using a fresh spec the prompt
   has never seen (TRAVEL, distinct from LEAVE / PURCHASE).
2. **Self-verify review_checklist v0.2** by running each ⚙ check against the
   generated code and confirming it would pass (or catching gaps).

## Generation summary

Generated under prompt_template_v1.3 conventions:
- Domain: 4 files (TravelWorkflow, TravelCase, TravelState, TravelEvents)
- Persistence: TravelCaseConfiguration + AddTravel migration (3 files)
- Application: 4 commands (Submit/Approve/Reject/Book) + 2 queries +
  3 services (ApprovalResolver, DecisionEvaluator, NotificationEmitter) + DTO
- Api: TravelController with 6 endpoints
- Frontend: TravelForm.tsx, TravelView.tsx, travelApi.ts, App.tsx hash route,
  AppLayout dropdown entry, workflow.ts FORMS entry
- Tests: 10 passing (decision evaluator parametric × 3, approval resolver × 4,
  integration tc_1 / tc_2 / tc_3 × 3)

## Acceptance summary (RULE #8)

| Step | Status | Detail |
|---|---|---|
| (a) db update | ✓ | migration `20260503061629_AddTravel` applied |
| (b) API on :5290 | ✓ | `/health` returns healthy |
| (c) Vite on :5173 | ✓ | proxy `/api → :5290` working |
| (d) Chrome MCP attached | ✓ | `list_pages` returns selected page |
| (e) navigate without typing URL | ✓ | Create dropdown → Travel group → "Travel Request (差旅申請) SPEC" |
| (f) submit using tc_1 | ✓ | controlled inputs filled via prototype-setter, date inputs included |
| (g) case appears in list | ✓ | `GET /api/travel/cases` returns the case |
| (h) detail renders all fields | ✓ | a11y snapshot shows applicant / type / destination / cost / dates / purpose / audit |
| (i) screenshots in dir | ✓ | `step-e-home.png`, `step-f-form-tc1.png`, `step-h-completed.png` |
| (j) per-screenshot assertions | ✓ | this file |
| (k) servers shut down | (verifying at end of this run) | |
| 0 console errors | ✓ | empty |
| 0 4xx/5xx | ✓ | 7 fetches, all 2xx |
| no URL typed | ✓ | every navigation via clickable element |

## State-machine assertions (per spec.testCases[0])

`expectedPath`: start_1 → task_request → approval_manager → gateway_intl → task_admin_book → end_1

Observed via repeated GET on case `54a1f212-3662-44b5-90ea-874ff88fd5c0`:

| Action | Result state | Asserts |
|---|---|---|
| POST /cases (employee=u_wilson, domestic) | 1 PendingManagerApproval | currentApprover=u_wang_manager (direct_manager rule) |
| POST /approve (u_wang_manager) | 3 PendingAdminBook | gateway_intl short-circuit: domestic → e4, skip VP. vpApprover=null |
| POST /book (u_admin_lead, TPE-2026-0001) | 4 Completed | adminBookerUserId=u_admin_lead, ticketRef=TPE-2026-0001 |

Matches spec.testCases[0].expectedApprovers: only approval_manager=u_wang_manager.

## checklist v0.2 self-verification — mechanical run

| § | Check | Result |
|---|---|---|
| 1.1 | flowCode → file naming (Travel*.cs files) | PASS — 10 Travel*.cs files found |
| 1.7 | bilingual labels in form | PASS — all 7 `Field` labels follow `中 / English` |
| 2.1 | no tenant in business logic | PASS — 0 hits of `"acme"` in Domain/Application |
| 3.1 | Travel in dropdown | PASS — appears in TRAVEL group |
| 3.5 | FormCode includes TRAVEL | PASS — type union + FORMS map |
| 3.6 | Vite proxy `/api → :5290` (v0.2) | PASS — present in vite.config.ts |
| 3.7 | controller route prefix matches flowCode | PASS — `[Route("api/travel")]` |
| 3.8 | personaToActingUserId multi-tier (v0.2) | PASS — present (manager → u_chen_vp on state=2) |
| 4.1 | 3 migration files committed | PASS — `.cs`, `.Designer.cs`, `AppDbContextModelSnapshot.cs` |
| 4.2 | DbSet in both contexts | PASS — both AppDbContext + IAppDbContext have `DbSet<TravelCase>` |
| 4.4 | Api.csproj CopyToOutputDirectory (v0.2) | PASS — identity-acme.csv has it |
| 4.5 | appsettings.json Identity:CsvPath (v0.2) | PASS |
| 6.1 | Resolver/Emitter AddScoped | PASS — 2 hits |
| 6.2 | Persistence DI three pieces | PASS — IAppDbContext + IIdentityProvider + INotificationSender all registered |
| 6.3 | Application.csproj EFC ref (v0.2) | PASS |
| 7.1 | dotnet test all green | PASS — 10/10 |
| 7.2 | spec.testCases full coverage (v0.2 mechanical) | PASS — 3 spec testCases × 3 `Tc{N}_` test methods |

**0 fails.** v0.2 checklist applied mechanically catches everything we tracked.

## Step (k) self-verification

This run **partially failed** step (k) at the start: when round 4 began, port
5173 (vite) and Chrome MCP were still up from the prior wizard sprint-1
verification. Port 5290 was clean. So step (k) "next round inherits empty
ports" held only for the API process, not for vite + Chrome.

This is **not** the prompt's fault — sprint-1 was a non-dogfood task, and
step (k) is currently scoped to "after a dogfood walk-through completes". But
it suggests v1.4 candidate: broaden step (k) to "any session that starts
servers should kill them before yielding to the next session".

Closing this round, I will kill all 3 (API + vite + Chrome) per step (k) so
the next round inherits clean state.

## Findings → prompt v1.4 candidates

1. **Step (k) scope creep.** Currently bound to dogfood end. Wider scope —
   any task that starts dev servers (sprint work, debugging, exploration) —
   should also clean up. Consider rewording (k) to "any time you start
   `dotnet run` or `npm run dev`, you must kill it before the conversation
   yields, regardless of task type."

2. **Validator error message convention.** This round's tc_3 validator
   message is `"EstimatedCost must satisfy 0 < value <= 1,000,000"`. spec.
   testCases[2].expectedValidationErrors[0] is the same string. v0.2 §1.6
   requires the message reference the spec key (here `value`-based not
   `EstimatedCost`-based). This was caught by virtue of testing — would v0.2
   automatically match a generator that wrote `Amount must be > 0` instead?
   Probably not — the check is `👁` (LLM judge) not `⚙`. **No prompt change
   needed**, but a future ⚙ rule could grep test source for the exact
   `expectedValidationErrors[0]` string.

3. **Wizard's spec output reaches Submit, but no test exists yet that
   compares wizard-exported spec.json to sample_specs/.** Sprint 2 work
   should add a click-through test that Submit produces a JSON byte-equal
   (modulo timestamps) to the corresponding `sample_specs/{flow}_v1.json`.
