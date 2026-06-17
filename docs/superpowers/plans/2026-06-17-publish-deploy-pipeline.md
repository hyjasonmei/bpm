# Publish→Deploy Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make "going live" a real, visible, gated pipeline — pressing **Publish** actually deploys the customer's stack (build + `az` deploy) and only marks the flow **Published** after the deploy succeeds, so the launcher never shows a flow whose code isn't deployed.

**Architecture:** Two phases. The end-to-end model is a clean alternation of **3 human gates** (admin: submit → approve → publish) interleaved with **3 deterministic poll steps** (agent: cook → merge → deploy). **Phase 1** makes the deploy step *visible* in the flow state machine without automating it: split Publish into `Publishing → Published/PublishFailed` and surface the new states in admin-ui; Approve stays a pure approval (no merge coupling); merge + deploy stay manual operator steps (existing `infra/azure/03-deploy.sh`). **Phase 2** automates the two poll steps: (a) the agent ff-merges the rebased cook branch into `main` on poll (local mode — no more "remind only"), and (b) a **deterministic** `PublishManager` (sibling of `PrManager` — git/`az` only, **no LLM**) builds `main` and runs `az` deploy when a flow is `Publishing`, then flips it to `Published` (success) or `PublishFailed`. Per-env deploy resource names live in admin Site Setting alongside the existing chef-token.

**Tech Stack:** C# .NET 10 (bpm-admin-svc Clean Arch, `Bpm.ChefAgent` console), React 18 + Vite (bpm-admin-ui), Azure CLI (`az webapp deploy`, `swa deploy`), EF Core + Postgres.

**Why deploy lives at Publish (not Approve):** at Approve time the cook branch is not yet on `main`, so a build of `main` wouldn't contain the flow — deploy can only run post-merge. And in flowcook's per-customer model, Approve = "code accepted into the product (merge to main)" while Publish = "deploy + activate on *this* customer's stack." Deploy is therefore a Publish-side, per-customer action.

**Non-goals (this plan):** rollback to a previous deploy (Phase 3); per-flow partial deploy (deploy is always whole-`main`-stack); zero-downtime deploy (the ~30–60s App Service cold-start window is accepted and surfaced via the `Publishing` state).

---

## Background / current state (read first)

- Flow state machine today (admin-svc `Flow` aggregate): `Draft → Submitted → Cooking → Committed → Approved → Published`. `Approved→Published` is gated on `MergedAt` being stamped. Branch merge is a manual operator step; `MergedAt` is set by the agent's `PrManager` (gh PR mode) or the admin-ui **Mark merged** button (remote-less).
- `Publish` today only flips the flow row to `Published` (launcher visibility via `/api/flow-registry`). **It deploys nothing.** Pressing it while the cooked code is not on the running stack yields a "visible but broken" flow (launcher shows it; the form/API 404).
- The chef-agent poll (`Bpm.ChefAgent/Program.cs`) already runs, per enabled env: `PrManager.ProcessAsync` (deterministic merge checks, no LLM) for every `ApprovedAwaitingMerge` flow, then `TaskPlanner.Plan` picks **one** cook (LLM `CookRunner.RunAsync`). `PublishManager` will be a third, deterministic step in the same loop.
- Per-env agent config lives in `chef/agent/chef-agent.json` (`EnvTarget`: name/baseUrl/chefToken/enabled). Per-env *product* settings (git mode etc.) are planned for admin Site Setting. Deploy config joins Site Setting.
- Deploy mechanics already proven manually: `dotnet publish -c Release` → zip → `az webapp deploy -n <app> -g <rg> --src-path <zip> --type zip --track-status false`, then **`az webapp restart`** (required — `--track-status false` does not load new code until restart) → health-poll; frontends `npm run build` (with `VITE_*` baked) → `swa deploy <dist> --deployment-token <token>`. Reference: `infra/azure/03-deploy.sh`.

---

## File Structure

**Phase 1 — state machine + visibility (admin-svc + admin-ui)**
- Modify `bpm-admin-svc/src/Bpm.Admin.Domain/.../FlowState.cs` (or wherever the flow-state enum lives) — add `Publishing`, `PublishFailed`.
- Modify the admin-svc Flow application service (the Approve/Publish transition logic) — Approve folds in merge-to-main; Publish moves `Approved/MergedAt → Publishing` (not straight to `Published`); add `MarkPublished` / `MarkPublishFailed` transitions.
- Modify the admin-svc Flows controller — `POST /api/.../publish` returns the flow now in `Publishing`; add internal/agent endpoints `POST /api/chef/flows/{id}/published` and `/publish-failed` (chef-token auth, like the existing chef merge endpoints).
- Modify `bpm-admin-ui` Serve/Publish tab — render `Publishing` (spinner + "部署中") and `PublishFailed` (red + retry), poll for transition.

**Phase 2 — automated deploy (chef-agent + Site Setting)**
- Create `chef/agent/Bpm.ChefAgent/PublishManager.cs` — deterministic build + `az` deploy + health-check, sibling of `PrManager.cs`.
- Create `chef/agent/Bpm.ChefAgent/PublishManager.Tests` (in existing test project) — pure-logic tests (state decisions, cooldown, command assembly).
- Modify `chef/agent/Bpm.ChefAgent/Program.cs` — add the `Publishing` sweep to the poll loop.
- Modify `chef/agent/Bpm.ChefAgent/Models.cs` + `AdminApiClient.cs` — `Publishing` task list + `MarkPublishedAsync`/`MarkPublishFailedAsync`.
- Modify admin-svc — `GET /api/chef/flows/tasks` returns a `Publishing` group; per-env DeployConfig entity + Site Setting CRUD.
- Modify `bpm-admin-ui` Site Setting — per-env deploy-config editor (App Service names, SWA tokens, az auth mode).

---

## PHASE 1 — Make the deploy step visible (no automation yet)

### Task 1: Add `Publishing` + `PublishFailed` flow states

**Files:**
- Modify: `bpm-admin-svc/src/Bpm.Admin.Domain/Flows/FlowState.cs` (confirm exact path via `grep -rn "enum FlowState" bpm-admin-svc/src`)
- Test: `bpm-admin-svc/tests/Bpm.Admin.*.Tests/.../FlowStateTransitionTests.cs`

- [ ] **Step 1: Write the failing test** — assert the new states exist and the legal transitions are `Approved→Publishing`, `Publishing→Published`, `Publishing→PublishFailed`, `PublishFailed→Publishing` (retry).

```csharp
[Fact]
public void Publish_moves_approved_flow_to_Publishing_not_Published()
{
    var flow = FlowTestData.Approved(mergedAt: DateTime.UtcNow);
    flow.Publish(now: DateTime.UtcNow);
    Assert.Equal(FlowState.Publishing, flow.State);
}

[Fact]
public void MarkPublished_only_from_Publishing()
{
    var flow = FlowTestData.Approved(mergedAt: DateTime.UtcNow);
    Assert.Throws<InvalidFlowTransition>(() => flow.MarkPublished(DateTime.UtcNow));
}
```

- [ ] **Step 2: Run test, verify it fails** — `dotnet test bpm-admin-svc --filter FlowStateTransition` → FAIL (states/methods missing).
- [ ] **Step 3: Add enum values** — add `Publishing` and `PublishFailed` to `FlowState` (after `Approved`, before `Published`); keep existing numeric values stable, append new ones so existing DB rows are unaffected.
- [ ] **Step 4: Implement transitions** — see Task 2 (the methods live on the Flow aggregate / service). Re-run after Task 2.
- [ ] **Step 5: Commit** — `git add ...; git commit -m "feat(admin): add Publishing/PublishFailed flow states"`

> Note: `FlowState` is persisted as an int/string column. Appending enum members is additive (no migration needed if stored as int and you append; if stored as string, also no migration). Confirm the column mapping in the Flow EF configuration before committing.

### Task 2: Publish transition → `Publishing`; add MarkPublished / MarkPublishFailed

**Files:**
- Modify: the admin-svc Flow aggregate/service holding `Approve`/`Publish` (find: `grep -rln "Publish" bpm-admin-svc/src/Bpm.Admin.Application bpm-admin-svc/src/Bpm.Admin.Domain`)
- Test: same test file as Task 1.

- [ ] **Step 1: Write failing tests** — `Publish()` requires `MergedAt != null` and state `Approved` (or `PublishFailed` for retry) → sets `Publishing`; `MarkPublished()` requires `Publishing` → sets `Published` + `PublishedAt`; `MarkPublishFailed(reason)` requires `Publishing` → `PublishFailed` + stores reason.

```csharp
[Fact]
public void Publish_requires_merged()
{
    var flow = FlowTestData.Approved(mergedAt: null);
    Assert.Throws<ConflictException>(() => flow.Publish(DateTime.UtcNow));
}
```

- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** the three methods on the aggregate (guard state + set fields). `PublishFailedReason` is a new nullable string column → add to the Flow entity + EF config + a migration (`dotnet ef migrations add Flow_PublishStates` in admin-svc, run with `BPM_DB_PROVIDER=postgres`).
- [ ] **Step 4: Run tests → PASS.**
- [ ] **Step 5: Commit.**

> ⚠️ admin-svc EF design-time factory defaults to SQLite; generate the migration with `BPM_DB_PROVIDER=postgres dotnet ef migrations add ...` or it rewrites the Postgres schema as TEXT (known trap, see chef-automation memory).

### Task 3: Approve stays a pure approval (NO change)

**Decision (resolved 2026-06-17):** Approve does **not** fold in the merge. The operator's mental model is a clean alternation — Approve is a human gate (`Committed → Approved`), and the **merge is a poll step done by the agent** (see Task 8b in Phase 2). So Phase 1 needs no Approve change; merge stays manual in Phase 1 (operator ff-merges + Mark merged) and becomes automatic in Phase 2.

- [ ] No work. Confirm the existing `Approve` transition is unchanged and `MergedAt` remains the deploy-readiness gate.

### Task 4: admin-ui — render Publishing / PublishFailed

**Files:**
- Modify: `bpm-admin-ui/src/flowcook/pages/aiKitchen/ServePanel.tsx` (the Serve tab; confirm name via `grep -rln "Publish" bpm-admin-ui/src/flowcook`)
- Modify: `bpm-admin-ui/src/flowcook/api/flows.ts` (FlowState type union — add `Publishing`, `PublishFailed`)

- [ ] **Step 1:** Add `'Publishing' | 'PublishFailed'` to the `FlowState` TS union.
- [ ] **Step 2:** In the Serve tab, when `state === 'Publishing'`: show a spinner + "部署中…（建置 + 部署到雲端，約數分鐘，期間服務會短暫重啟）" and poll the flow every 5–10s until it leaves `Publishing`.
- [ ] **Step 3:** When `state === 'PublishFailed'`: red banner with `publishFailedReason` + a **重試部署** button that re-calls Publish (→ `Publishing`).
- [ ] **Step 4:** Publish button: on click, call publish endpoint, optimistic-set local state to `Publishing`.
- [ ] **Step 5:** `npx tsc -p tsconfig.app.json --noEmit` → 0 errors; manual boot + screenshot; commit.

**Phase 1 done = Publish is honest:** pressing Publish shows `Publishing`; the operator runs the deploy manually (`infra/azure/03-deploy.sh` or targeted), then (Phase 1 interim) clicks a "Mark deployed" affordance / the agent flips it. The flow only reaches `Published` after deploy. No more "visible but broken."

---

## PHASE 2 — Automate the deploy in the poll (deterministic PublishManager)

### Task 5: Per-env DeployConfig in Site Setting (admin-svc)

**Files:**
- Create: `bpm-admin-svc/src/Bpm.Admin.Domain/Settings/EnvDeployConfig.cs`
- Modify: admin Site Setting entity/service + controller + migration.
- Test: settings service tests.

- [ ] **Step 1:** Define the config shape (one row per env):

```csharp
public sealed class EnvDeployConfig
{
    public string EnvName { get; set; } = "";        // matches chef-agent EnvTarget.Name
    public string ResourceGroup { get; set; } = "";
    public string BpmSvcApp { get; set; } = "";       // App Service name
    public string AdminSvcApp { get; set; } = "";
    public string BpmUiSwa { get; set; } = "";
    public string AdminUiSwa { get; set; } = "";
    // SWA deploy tokens are secrets → store encrypted (see ssh-key precedent), or
    // resolve at deploy time via `az staticwebapp secrets list`. Prefer the latter:
    // store nothing secret here, let the worker's `az` identity fetch tokens.
    public bool Enabled { get; set; }
}
```

- [ ] **Step 2–5:** CRUD endpoint (`GET/PUT /api/site-setting/deploy-config`), migration (`BPM_DB_PROVIDER=postgres`), tests, commit. **Secrets:** do NOT store SWA tokens / az creds in the DB; the worker uses its logged-in `az` + fetches SWA tokens at deploy time (matches how `03-deploy.sh` already does `az staticwebapp secrets list`). DB holds only resource names.

### Task 6: Agent — `Publishing` task list + Mark endpoints

**Files:**
- Modify: `bpm-admin-svc/.../ChefFlowsController.cs` — `GET /api/chef/flows/tasks` adds a `publishing` group; add `POST /api/chef/flows/{id}/published` + `/publish-failed` (chef-token auth).
- Modify: `chef/agent/Bpm.ChefAgent/Models.cs` — `ChefTasks.Publishing` list.
- Modify: `chef/agent/Bpm.ChefAgent/AdminApiClient.cs` — `MarkPublishedAsync`, `MarkPublishFailedAsync(reason)`.
- Test: `ChefTasksEndpointTests` (admin-svc), `AdminApiClient` is thin (covered by agent integration).

- [ ] **Steps:** TDD the endpoint (returns flows in `Publishing` for the env), then the agent client methods. Auth: reuse the chef-token `Authorization: Bearer` scheme (per memory: chef API auth is Bearer, NOT X-Chef-Token). Commit.

### Task 7: `PublishManager` (deterministic build + az deploy)

**Files:**
- Create: `chef/agent/Bpm.ChefAgent/PublishManager.cs`
- Test: `chef/agent/Bpm.ChefAgent.Tests/PublishManagerTests.cs`

Interface (mirrors `PrManager`):

```csharp
public sealed class PublishManager
{
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(30);
    // Process one Publishing flow for one env: build main, az-deploy, health-check,
    // then Mark{Published|PublishFailed}. Idempotent + cooldown-gated so a poll that
    // lands mid-deploy doesn't start a second one.
    public async Task ProcessAsync(EnvTarget env, EnvDeployConfig cfg, AdminApiClient api, ChefTask task, CancellationToken ct);

    // Pure, unit-tested:
    public static bool ShouldStartDeploy(DateTime now, DateTime? lastAttemptAt, bool inFlight); // cooldown + single-flight
}
```

- [ ] **Step 1: Write pure-logic failing tests** — `ShouldStartDeploy` returns false when `inFlight`, false within `RetryCooldown` of `lastAttemptAt`, true otherwise.

```csharp
[Fact]
public void ShouldStartDeploy_false_when_in_flight()
    => Assert.False(PublishManager.ShouldStartDeploy(T0, lastAttemptAt: null, inFlight: true));

[Fact]
public void ShouldStartDeploy_false_within_cooldown()
    => Assert.False(PublishManager.ShouldStartDeploy(T0.AddMinutes(5), lastAttemptAt: T0, inFlight: false));
```

- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement `ShouldStartDeploy`** (single-flight + cooldown).
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Implement `ProcessAsync`** — deterministic, no LLM:

```
1. guard: ShouldStartDeploy(now, state.LastDeployAt[flowId], state.DeployInFlight) else return
2. mark in-flight (state) + git -C repo checkout main && git pull/verify HEAD has the flow code
3. backend: dotnet publish bpm-svc + admin-svc -c Release -o <tmp>; zip; 
   `az webapp deploy -n <cfg.BpmSvcApp> -g <cfg.ResourceGroup> --src-path <zip> --type zip --track-status false`;
   `az webapp restart -n <cfg.BpmSvcApp> -g <cfg.ResourceGroup>`; health-poll /health until 200 (≤120s)
4. frontend: npm run build (VITE_* baked from resolved hostnames); 
   `swa deploy <dist> --deployment-token $(az staticwebapp secrets list ...) --env production`
5. on all-green → api.MarkPublishedAsync(flowId); TG "✅ deployed + published"
   on any failure → api.MarkPublishFailedAsync(flowId, reason); TG "🛑 publish/deploy failed: <reason>"
6. clear in-flight (state)
```

- [ ] **Step 6: Commit** (manager + tests).

> Build/deploy commands are exactly those validated in `infra/azure/03-deploy.sh` and this plan's Background section. Reuse `ProcessRunner` (the agent's existing shell wrapper) for `git`/`dotnet`/`az`/`swa`.

### Task 8: Wire `PublishManager` into the poll loop

**Files:**
- Modify: `chef/agent/Bpm.ChefAgent/Program.cs`

- [ ] **Step 1:** After the `PrManager` loop and before `TaskPlanner.Plan`, add: load the env's `EnvDeployConfig` (from a small `GET /api/site-setting/deploy-config?env=` call or the agent config), then `foreach (var task in tasks.Publishing) { await publishManager.ProcessAsync(env, cfg, api, task, ct); break; }` — **one deploy per poll** (like one-cook-per-poll), to serialize migrations + the cold-start window.
- [ ] **Step 2:** Single-flight across polls via `AgentState` (`DeployInFlight` + `LastDeployAt[flowId]`), persisted in `agent-state.json`.
- [ ] **Step 3:** `dotnet test chef/agent` → green; commit.

### Task 8b: Agent auto-ff-merges the cook branch on poll (local mode)

**Files:**
- Modify: `chef/agent/Bpm.ChefAgent/PrManager.cs` (the no-remote branch of `ProcessAsync`)
- Test: `chef/agent/Bpm.ChefAgent.Tests/PrManagerTests.cs`

**Why:** today, for an `ApprovedAwaitingMerge` flow with no gh remote, `PrManager` only posts a "merge manually" memo (the `IsAncestorOfMainAsync` auto-detect path is unreachable without a PR url). The operator's flow wants the **poll** to do the merge (step 4). So in local mode the agent should ff-merge the rebased cook branch into `main` itself, then stamp `MergedAt`.

- [ ] **Step 1: Failing test** — given a cook branch that is ahead of `main` and ff-mergeable, the local-mode path calls `git merge --ff-only <branch>` into main and then `MarkMergedAsync`. Given a non-ff-able branch, it falls back to the reminder memo (current behavior).

```csharp
[Fact]
public void LocalMerge_attempts_ff_only_when_branch_ahead()
    => Assert.True(PrManager.ShouldAutoFfMerge(branchAheadOfMain: true, ffPossible: true));

[Fact]
public void LocalMerge_falls_back_to_remind_when_not_ff()
    => Assert.False(PrManager.ShouldAutoFfMerge(branchAheadOfMain: true, ffPossible: false));
```

- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement** — in the no-remote branch: check `git merge-base --is-ancestor main <branch>` (ff-able) → `git -C repo checkout main` → `git merge --ff-only <branch>` → on success `api.MarkMergedAsync(flowId, "agent-ff-merge")` + TG "✅ merged"; on non-ff or conflict → keep the existing reminder memo (24h cooldown). The merge is into **local** main only (no push; GitKraken pushes to GitHub separately — local main is what the Phase 2 deploy builds from). Single-flight + guard so it doesn't re-merge an already-merged flow.
- [ ] **Step 4: Run → PASS.**
- [ ] **Step 5: Commit.**

> The cook branch is rebased onto main during the cook (proven clean for WFH V1), so `--ff-only` should succeed. `--ff-only` is the safety rail: never auto-create a merge commit / never auto-resolve conflicts — fall back to the human reminder if it can't fast-forward.

### Task 9: admin-ui Site Setting — per-env deploy-config editor

**Files:**
- Modify: `bpm-admin-ui/src/flowcook/pages/SiteSettingPage.tsx` (+ a new tab/section)

- [ ] **Steps:** form to edit `EnvDeployConfig` per env (resource names + enabled toggle; no secrets). `tsc` + manual boot + commit.

---

## Self-review checklist (done)

- **Spec coverage:** deploy-at-Publish ✓ (Task 2), Publishing state ✓ (Tasks 1/2/4), Approve-folds-merge ✓ (Task 3, gated on operator confirm), agent deterministic deploy ✓ (Tasks 7/8), per-env config in Site Setting ✓ (Tasks 5/9), whole-stack-not-per-flow ✓ (Task 7 builds main), cold-start surfaced ✓ (Task 4 copy + Publishing state), failure→PublishFailed ✓ (Tasks 2/7), no-LLM-for-deploy ✓ (Task 7 is deterministic).
- **Open decisions flagged:** Task 3 (how tightly Approve couples to merge) needs operator sign-off before coding.
- **Deferred (not in this plan):** rollback to previous deploy; per-flow partial deploy; zero-downtime.

## Phasing recommendation

- **Phase 1 (Tasks 1, 2, 4; Task 3 is a no-op):** ship first. Low risk, no agent/az changes. Immediately fixes "Publish lies" by making `Publishing`/`PublishFailed` real; merge + deploy stay manual.
- **Phase 2 (Tasks 5, 6, 7, 8, 8b, 9):** the agent automation — both the auto-ff-merge poll step (8b) and the auto-deploy `PublishManager` (5–8). Requires per-env deploy config + careful single-flight + the accepted cold-start window. Ship after Phase 1 is proven.

**End-to-end target (matches the operator's test flow):** submit →[poll: cook]→ Committed → approve →[poll: agent ff-merge → MergedAt]→ publish →[poll: PublishManager build+az deploy]→ Published.
