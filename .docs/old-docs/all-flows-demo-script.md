# All Flows Real — 5-Minute Demo Script

Audience: sales partner / customer rep watching a single 5-minute walkthrough.
Goal: prove every demo flow is a real `ProcessInstance`, not a UI mock.

## Setup (one-time)

```bash
git clone <repo> && cd bpm
dotnet run --project bpm-svc/src/SeedCli -- reset
dotnet run --project bpm-svc/src/SeedCli -- seed --include-bundles
dotnet run --project bpm-svc/src/Api &        # serves :5290
npm --prefix bpm-ui run dev &                 # serves :5173 (employee app)
npm --prefix bpm-admin-ui run dev &           # serves :5174 (admin console)
```

`reset` drops `bpm.db` and re-applies migrations. `seed --include-bundles`
loads the 13-persona organisation + installs every `sample_specs/*.json`
as a `SpecBundle` in `Pending` status (open Flow Library and click
"Repro Check" to flip to `Validated`).

## 11 flows you can demo

| Code   | Name              | Personas (chain)                                     | Talking point                                          |
|--------|-------------------|------------------------------------------------------|--------------------------------------------------------|
| LEAVE  | 請假申請          | Wilson → Yang → Mary                                  | Simplest flow; fastest to demo                          |
| GEE    | 員工費用申請      | Wilson → Yang → Sue × 2                               | Two finance steps (confirm + review)                    |
| GEV    | 廠商費用申請      | Wilson → Yang → Sue × 2                               | Vendor invoice with VAT + file upload                   |
| APE    | 預支費用          | Wilson → Yang → Sue × 2                               | Cash advance with charge-dept routing                   |
| HWP    | 硬體採購          | Wilson → Lin × 2 → Yang × 2 → Sue                     | 7-step purchase flow                                    |
| ITPR   | IT 軟體採購        | Wilson → Lin × 2 → Yang × 2 → Sue                     | Same skeleton as HWP, SaaS-flavoured                    |
| TRQ    | 差旅申請          | Wilson → Yang → Pat                                   | Admin task to book travel after manager approves        |
| TEO    | 差旅費報銷        | Wilson → Yang → Sue × 2 (+ extra approvers ≥50K)      | Threshold gateway → any-2-of-3 collection               |
| EXTOB  | 外部到職          | Yang (manager) → Mary (HR)                            | Submitter is `role:manager`; HR creates the AD account  |
| RESIGN | 離職申請          | Wilson → Yang → Mary                                  | Coexists with legacy `HrFlowsController`                |
| DEPTX  | 部門異動          | Wilson → Yang → Mary                                  | Coexists with legacy `HrFlowsController`                |

(Persona shorthand: Wilson = employee, Yang = engineering manager, Mary = HR,
Sue = finance, Lin = IT, Pat = office admin. Full mapping in
`PersonaSeedService.cs`.)

## The "everything is real" demo (3 minutes)

1. Open employee app (`:5173`) → log in as Wilson via RoleSwitcher.
2. Click "Quick action: Leave Request" on Home.
3. Fill 5-day vacation → Submit.
4. Toast confirms `Submitted! Instance ID: ...` — this is now a real
   `ProcessInstance` row backed by `SpecSnapshot`.
5. Switch persona to Yang via RoleSwitcher → open Inbox.
6. Click the LEAVE task → form opens in **task mode** (Approve / Reject /
   Return buttons + comment dialog).
7. Approve with comment "go enjoy".
8. Switch to Mary → archive the HR userTask.
9. Open admin console (`:5174`) → Process Admin → Live Cases. The case
   moves to Completed Cases (status `Completed`).
10. Open admin console → Sandbox Mailbox → see the captured `on_assign` /
    `on_complete` notifications (sandbox mode is on by default in dev).

## The "sandbox UAT" demo (5 minutes)

Builds on the previous demo:

1. Open Sandbox Mailbox → Clock tab → advance time +1 day. SLA timers
   (when wired) fire; until then this just advances `SandboxClock.UtcNow`.
2. Reset state via `dotnet run --project bpm-svc/src/SeedCli -- reset` (or
   from Process Admin → Live Cases → Reset, when wired).
3. Re-seed + reproduce. Sandbox mode means no external mail / webhook /
   SMS is ever sent — `OutboundGate` swallows everything in capture-only
   mode.
4. Repeat with any of the other 10 flows. The "Approve in inbox →
   advance to next persona → see captured notification" loop is
   identical regardless of flow code.

## What's actually different from before

Before PR-L1..L6: the 11 forms were UI mocks. Submitting did nothing on
the server; persona switching just changed labels.

After: every form starts a real `ProcessInstance`, drives through
`ProcessRuntime` (`StartInstanceAsync` → `SubmitTaskAsync` chain),
captures notifications via `SandboxCapturingNotificationDispatcher` when
sandbox is on, and shows up in `Live Cases` / `Completed Cases` /
`Reports`. The 11 `sample_specs/*.json` are the only source of truth for
flow shape — change a step's actor or threshold and the next instance
picks it up automatically.

What's still mocked: `Report.tsx` charts (waiting on `add-real-reporting`),
`Activity Feed` and `Reminders` widgets on Home (small `demo` tag in the
corner).
