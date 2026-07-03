# flowcook MVP demo runbook

End-to-end runbook for the first chef demo (LEAVE flow). 開發者-facing —
this is the operational walk-through; the chef-side detail lives in
`chef/skill/SKILL.md` + `chef/skill/workflow.md`.

If something here disagrees with `chef/skill/*`, treat the skill as
source of truth and update this file.

## What MVP demo proves

Three things, in one continuous loop:

1. **admin** (AI Kitchen) — 開發者 walks an 11-step wizard, AI helps fill
   detail, output is a portable `.zip` bundle containing
   `spec.json` + form layout + sample org + test cases.
2. **chef** (manual Claude session on a fresh branch in the normal
   checkout) — reads the bundle and writes per-version code under
   `bpm-svc/src/{Persistence,Api}/Features/<CODE>/V<N>/**` +
   `bpm-ui/src/features/<CODE>/V<N>/**`, runs the migration + seed
   against the shared db, drives Chrome through `/apply/<CODE>` to
   verify the happy path.
3. **bpm** (employee app + runtime) — the chef-generated form renders
   on the bpm-ui side; submit flows through ProcessRuntime;
   notifications fire and land in `NotificationDispatchAudits` for
   the admin Process Admin Console to surface.

## Pre-requisites (one-time)

```bash
mkdir -p ~/claude/flowcook-bundles
```

The repo must be on a branch that has the Phase 1 + Phase 2 work
landed (today's `main` after merge `b5722b0` is fine; or
`prioritize-openspec`). If you're on an older branch checkout
something current first:

```bash
cd ~/claude/bpm
git checkout main
git pull
```

Verify the AI backend you'll use. `admin-svc` defaults to
`FLOWCOOK_AI_BACKEND=cli` which borrows your Claude Code subscription
— make sure `claude` is logged in. For the `api` path you'll need
`ANTHROPIC_API_KEY` exported.

## Step 1 — Wipe stale db files (post Phase 1.1 db merge)

The Phase 1.1 commit merged admin + bpm onto one shared `db/bpm.db`
file. If your main checkout still has the old separate `admin.dev.db`
or a `db/bpm.db` from before, wipe them so both services start clean:

```bash
cd ~/claude/bpm
rm -f db/bpm.db db/bpm.db-shm db/bpm.db-wal
rm -f bpm-admin-svc/src/Bpm.Admin.Api/admin.dev.db
rm -f bpm-admin-svc/src/Bpm.Admin.SeedCli/admin.dev.db
```

## Step 2 — Boot the main stack (cook the spec)

Four processes, four terminals (or your tmux of choice):

```bash
# Terminal A — bpm-svc API (port 5290)
cd ~/claude/bpm/bpm-svc
dotnet run --project src/Api

# Terminal B — bpm-admin-svc API (port 5266)
cd ~/claude/bpm/bpm-admin-svc
dotnet run --project src/Bpm.Admin.Api

# Terminal C — bpm-admin-ui (port 5174)
cd ~/claude/bpm/bpm-admin-ui
npm run dev

# Terminal D — bpm-ui (port 5175)
cd ~/claude/bpm/bpm-ui
npm run dev
```

First boot (post unify-user-store):
- `admin-svc` auto-migrates `Admin_*` tables into `db/bpm.db` AND
  auto-seeds the unified org (13 users / 6 depts / 14 roles +
  dept-heads + user-managers) the first time `Admin_Principals` is
  empty. **This is now the single identity seed for the whole
  system.** Login emails: `alice@acme.example` / `bob@acme.example` /
  … `mia@acme.example`. Password: `flowcook2026`.
- `bpm-svc` auto-migrates its runtime tables. It no longer has its
  own user / principal / role tables — the runtime reads admin's
  identity store via `SharedIdentity` entity mappings.
- `bpm-ui` (port 5173) shows a real Login screen on first hit.
  Email + password lands a JWT via `POST /api/auth/login`. The dev
  IdentitySwitcher dropdown still exists and resolves persona codes
  to seeded admin users via `/api/dev/login` (gated on
  `BPM_AUTH_MODE != prod`).

Verify all four ports listen:

```bash
lsof -i :5266 -i :5290 -i :5174 -i :5173 | grep LISTEN
```

(bpm-ui defaults to Vite port `5173` — the launch script does not pin
it to `5175` anymore.)

### Reusing a prior bundle vs cooking fresh

Two paths into Step 5+ depending on whether you have a recent
`LEAVE_v1.zip` lying around:

- **Cook fresh** (recommended for the first run after unify): walk
  Step 3 below. The new bundle carries a `sample-org.json` referencing
  admin's `@acme.example` user UUIDs, so chef hits zero principal-UUID
  drift.
- **Reuse a pre-unify bundle**: the `spec.json` shape is unchanged so
  chef-side codegen still works, but the bundled `sample-org.json`
  references the retired `wilson@acme.test` user ids. chef's
  `principal-UUID drift` rule (chef/skill/conventions.md §6) tells it
  to rewrite UUIDs into the current seed — costs chef a handful of
  extra rewrites at cook time. Tolerable but skip-it-when-possible.

## Step 3 — Cook LEAVE in admin

1. Open <http://localhost:5174/ai-kitchen>
2. Click **Cook new flow**. flowCode = `LEAVE`, displayName = `請假申請`
3. Walk steps 1 → 11. Suggested first-flow choices:
   - **SOURCE**: paste "員工填單 → 主管核准 → HR 登錄" or pick the LEAVE preset
   - **FORMS**: one section, two-column row for start/end date, full-width reason. Skip Tier 2 (no repeater) for the first run — keeps chef's first scope bounded
   - **ACCESS**: launchable by `dept:Acme Corp` (the default)
   - **DECISIONS / APPROVERS**: use the spec_schema's `submitter.manager` actor path
   - **NOTIFY**: one `on_submit` notification to `current_approver`
   - **SLA**: 8h on the approval node with `notify` escalation
   - **NOTES**: anything chef should know (「請假天數扣除週六日」etc.)
4. **Download bundle** in the wizard top-right. The browser saves
   `LEAVE_v1.zip` to `~/Downloads`.
5. Unzip into the bundle staging area:

   ```bash
   cd ~/Downloads
   TODAY=$(date +%Y%m%d)
   unzip -o LEAVE_v1.zip -d ~/claude/flowcook-bundles/LEAVE-v1-$TODAY
   ls ~/claude/flowcook-bundles/LEAVE-v1-$TODAY
   # spec.json bpmn.xml manifest.json forms/ test-cases/ notifications/ ...
   ```

## Step 4 — Kill the running stack + create a chef branch

The dev stack uses fixed ports (5266 / 5290 / 5174 / 5175). chef will
boot its own dev server in step 6, so shut down anything already
listening:

```bash
lsof -ti :5266 :5290 :5174 :5175 | xargs -r kill
sleep 2
lsof -i :5266 -i :5290 -i :5174 -i :5175 | grep LISTEN   # should print nothing
```

Create a fresh branch in your normal checkout (no `git worktree` —
this MVP stays on the simple side):

```bash
cd ~/claude/bpm
git checkout main
git pull
git checkout -b leave-test-1     # or whatever name you want for this flow
```

## Step 5 — Start the chef session

From the repo root:

```bash
claude   # or your usual Claude Code invocation
```

Paste this prompt verbatim (substitute the bundle date):

```
你是 chef。執行以下流程：

1. 用 Skill 工具載入 chef-codegen skill
   （或讀 .claude/skills/chef-codegen/SKILL.md 全文）
2. 讀 chef/skill/SKILL.md 全文
3. 確認 bundle 路徑存在：
   bundles/LEAVE_v1-<把這替換成今天日期>
4. 讀 bundle/**，向我回報你的 plan
   （一段話：幾個 user task、幾個 approval、有沒有 integration、估計幾個 commit）
5. 等我說 go 才開始寫 code
6. 直接做完, build, test, e2etest (using chrome mcp with seed account)，有需要釐清才停下來
```

chef should reply with something like:

> Plan for LEAVE V1: 3 user tasks (apply / manager_approve / hr_record),
> 1 approval (manager), 1 on_submit notification, 0 integrations,
> est. 5-6 commits. Proceeding when you say go.

If the plan looks right, reply `go`. If chef missed something (e.g.
你的 SLA 設定沒進 plan），say so before letting it write code.

## Step 6 — chef writes code, migrates, seeds, smokes

chef should drive the chef branch end-to-end without your help:

- writes per-version code under
  `bpm-svc/src/{Persistence,Api}/Features/LEAVE/V1/**` plus the
  EF migration in `bpm-svc/src/Persistence/Migrations/` and tests
  under `bpm-svc/tests/Bpm.Tests/Features/LEAVE/V1/**`
- writes `bpm-ui/src/features/LEAVE/V1/**` (component +
  `manifest.ts` exporting `{ code: 'LEAVE', version: 1, component }`)
- runs `dotnet ef database update --project src/Persistence
  --startup-project src/Api` against the shared `db/bpm.db`
- (optional) `dotnet run --project bpm-svc/src/SeedCli -- seed
  --include-bundles` if you want the spec bundle pre-installed in
  the Flow Library. Identity rows are seeded by admin-svc on its
  first boot — bpm-svc no longer owns them.
- boots `dotnet run --project src/Api` and `npm run dev` (in
  `bpm-ui/`)
- logs into bpm-ui as `bob@acme.example` / `flowcook2026` via the
  real Login screen on `http://localhost:5173/` (the dev shortcut
  IdentitySwitcher also works post-login). Drives chrome-devtools
  through `/apply/LEAVE` — fills the form, submits, watches the
  ProcessInstance show up. Manager-approval step lands in
  `alice@acme.example`'s inbox (Bob's direct manager edge in
  `Admin_UserManagers`). HR step lands in `henry@acme.example`
  (HR dept head). Notification row writes to
  `NotificationDispatchAudits` for the admin Process Admin Console
  to surface (no `VITE_FEATURE_*` env var — the bpm-ui registry
  picks the manifest up automatically on dev-server start)

After each logical commit chef should report a one-liner:

> Commit 3 done — approval handler + tests passing (4/4).

If chef pauses on a stop-and-ask, answer or paste the question back
here so we can update the skill.

## Step 7 — Review + ship

```bash
cd ~/claude/bpm                  # already there — same checkout
git log --oneline main..HEAD
git diff main..HEAD --stat
```

Diff in GitKraken or `git diff`. Independent re-verifications:

```bash
cd bpm-svc && dotnet test --filter LEAVE_V1
cd ../bpm-ui && npx tsc -p tsconfig.app.json --noEmit
```

Take a screenshot of the running form for the demo deck.

When happy, push the chef branch with GitKraken (the chef session
can't ssh github — that's your job). Open a PR if you want
`/ultrareview` to take a pass; merge into `main` when satisfied.

## Step 8 — After merge (no extra migration needed)

Because chef wrote against the shared `db/bpm.db` in step 6, the
`<CODE>_V<N>_*` tables already exist by the time the branch lands
on `main`. Just re-boot the four-process stack from Step 2 and visit
`/apply/LEAVE` — chef's LEAVE_V1 component renders via the feature
manifest registry (no App.tsx edit, no env-var flag).

If you ever spin up a brand-new checkout (different machine, fresh
clone), the EF migrations auto-apply on `bpm-svc` startup.

## Step 9 — Clean up

```bash
cd ~/claude/bpm
git branch -d leave-test-1       # if merged
# or keep the branch if you want to compare against the next chef run
```

The chef session can be closed.

## Known footguns

1. **`/apply/LEAVE` shows NotCookedYet placeholder.** Either chef
   didn't write `manifest.ts`, or the dev server was running before
   chef created the `features/LEAVE/V1/` folder and Vite hasn't
   re-globbed it yet — kill + restart `npm run dev`.
2. **chef wants to edit `bpm-svc/Runtime/...` or `bpm-admin-*`.**
   Reject — that violates SKILL.md §1 rule 1. Tell chef the spec
   is asking for behaviour the runtime doesn't expose, and either
   the spec needs adjusting or the runtime needs a separate change
   request.
3. **`dotnet test` fails and chef wants to `[Skip]` the test.**
   Reject — SKILL.md §1 rule 6. The fix is in the implementation,
   not the test.
4. **Sandbox is on / off when you expect the opposite.** Check
   `TenantSettings.SandboxMode` in the bpm db; toggle from the
   Sandbox tab in admin if needed. Sandbox-on routes notifications
   to the in-app mailbox and writes `status: captured`; sandbox-off
   writes `status: dispatched` (log-only today — real send lands
   when the notification engine ships).
5. **CEL `sum(items.amount)` blows up in a repeater total.**
   Known Cel.NET 1.0.0 bug (see root CLAUDE.md follow-ups). LEAVE
   has no repeater so the first demo avoids it; HWP / GEE / GEV
   would trip on it. Plan: first demo = LEAVE, second demo = HWP
   only after the CEL bug is worked around.
6. **`__EFMigrationsHistory` collisions on shared db.** Should not
   happen after Phase 1.1 — admin has its own
   `__AdminEFMigrationsHistory` table. If you see this anyway,
   verify `bpm-admin-svc/src/Bpm.Admin.Api/Program.cs` still
   passes the `MigrationsHistoryTable("__AdminEFMigrationsHistory")`
   option to `UseSqlite`.

## Iteration loop (what to do when chef stumbles)

Categorise the failure and update the matching file on `main` (not
the chef branch — skill edits land separately from feature code so
they apply to every future chef run):

| Failure shape | Fix lives in |
|---|---|
| chef wrote the wrong path / forgot the prefix / skipped a step | `chef/skill/SKILL.md` or `chef/skill/conventions.md` |
| chef's workflow step needed a command that wasn't documented | `chef/skill/workflow.md` |
| spec was missing info chef genuinely needed | `openspec/specs/flowcook-wizard/spec.md` (and probably bump the wizard UI to capture it) |
| runtime / infra didn't support what chef tried | `bpm-svc/**` or `bpm-ui/**` accordingly |

Commit the skill / spec edit to `main`. On the chef branch:

```bash
git fetch
git rebase origin/main          # or merge if you prefer
```

Then in the chef session say: "skill 已更新，重讀 chef/skill/SKILL.md
然後繼續". chef picks up the new version.

## Cross-references

- `chef/README.md` — what chef is, where the skill lives
- `chef/skill/SKILL.md` — system-prompt source of truth (read by chef)
- `chef/skill/conventions.md` — naming / paths / flag / variables
- `chef/skill/workflow.md` — the chef-side step-by-step
- `.claude/skills/chef-codegen/SKILL.md` — Skill-tool entry point
- `openspec/specs/flowcook-chef/spec.md` — canonical rule set
- `openspec/specs/flowcook-wizard/spec.md` — what the spec means
- `openspec/changes/archive/2026-05-22-flowcook-mvp-chef-bootstrap/` —
  the proposal that landed this MVP
