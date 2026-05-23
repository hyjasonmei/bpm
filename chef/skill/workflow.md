# chef MVP workflow

The end-to-end run for a single flow version. Jason drives steps 0–3
manually; chef (Claude session) drives steps 4–6 inside a normal
checkout on a fresh branch; Jason reviews + ships in step 7.

There is **no separate git worktree** in this MVP. chef runs against
the same repo Jason works in, on a per-flow branch (e.g.
`leave-test-1`), and chef + main bpm stack share the same `db/bpm.db`.
That's intentional: simplest possible setup, no path juggling, no
"which checkout am I in?" confusion.

Convention: **only one dev server stack at a time** on the local box
(they all share ports 5266 / 5290 / 5174 / 5175).

## 0. Pre-flight (Jason)

Once per machine:

```bash
mkdir -p ~/claude/flowcook-bundles
```

Per chef session — shut any running dev stack down before chef boots:

```bash
lsof -ti :5266 :5290 :5174 :5175 | xargs -r kill
```

## 1. Author + freeze the spec (Jason, in admin wizard)

1. Open AI Kitchen, walk the 11 steps to completion.
2. Download bundle → unzip to
   `~/claude/flowcook-bundles/<FLOWCODE>-v<N>-<YYYYMMDD>/`.
3. Verify the unzipped tree contains `spec.json` + `bpmn.xml` +
   `manifest.json` + `forms/` + `sample-org.json` + `test-cases/`
   (see SKILL.md §2 for the full layout).

## 2. Create a chef branch (Jason, in the bpm repo)

```bash
cd ~/claude/bpm
git checkout main
git pull
BRANCH="<flowcode>-test-1"      # e.g. leave-test-1
git checkout -b "$BRANCH"
```

You're now on the chef branch in your normal checkout. Everything
chef does happens here; merge back to `main` after review.

## 3. Start chef session (Jason)

```bash
# From the repo root
claude
```

In the session, prime chef:

```
Read .claude/skills/chef-codegen/SKILL.md and follow it.
Bundle path: ~/claude/flowcook-bundles/LEAVE-v1-20260522
```

That's it — chef takes over from here.

## 4. Read + plan (chef)

chef reads in the order spelled out in SKILL.md §4. After reading,
chef states a one-paragraph plan back to Jason:

> Plan for LEAVE V1: 3 user tasks (apply / manager-approve / hr-record),
> 1 gateway (amount > 50k → CFO), 2 approvals, 2 notifications
> (on_submit, on_approve), no integrations. Tests: 3 form, 2 branch,
> 2 approve+reject, 1 e2e. Looks bounded — proceeding.

Jason accepts or pushes back. Once accepted, chef starts writing.

## 5. Generate + verify (chef)

### 5a. Generate

chef writes files commit-by-commit (see conventions §commit-shape).
After each commit, chef:

- Runs the relevant test (`dotnet test --filter <CODE>_V<N>` or
  `npx tsc -p tsconfig.app.json --noEmit`).
- Reports a one-liner: "Commit N done, M tests passing".

Failures: chef investigates the test, fixes the implementation (not
the test), commits the fix, re-runs. Never papers over with `[Skip]`.

### 5b. Migration + seed

`DbPathResolver` lands the SQLite db at `<repoRoot>/db/bpm.db`. chef
runs the migration against it directly:

```bash
cd bpm-svc
dotnet ef database update --project src/Persistence --startup-project src/Api
```

After unify-user-store the **user seed is owned by admin-svc** — boot
admin-svc once on a fresh db and it self-seeds (13 users
@acme.example with password `flowcook2026`, plus role / dept-head /
user-manager edges). bpm-svc's `PersonaSeedService` + SeedCli persona
seed are retired. SeedCli's `--include-bundles` is still useful for
seeding spec bundles into the Flow Library table but no longer
touches identity rows.

chef's `<CODE>_V<N>_*` tables now exist in the shared db. (Because
there's only one db file in this MVP, the migration sticks across
the branch switch — Jason doesn't need to re-run it after merge.)

### 5c. Boot dev servers + clickthrough

```bash
# Terminal A — bpm-svc API
cd bpm-svc && dotnet run --project src/Api

# Terminal B — bpm-ui
cd bpm-ui
npm run dev
# No feature-flag env var needed — the registry globs
# features/*/V*/manifest.ts on dev-server start. If you added the
# folder while Vite was already running and /apply/<CODE> still shows
# NotCookedYet, restart `npm run dev` so it picks up the new
# manifest module.
```

chef opens chrome-devtools (or asks Jason to open
`http://localhost:5175/apply/<CODE>`), submits the form, watches the
runtime accept the instance, and ticks the E2E checklist box. Any
visible regression vs. the reference form (`Reference_<Code>*.tsx`)
is flagged in the final report.

## 6. Final report (chef)

After the last commit chef tells Jason:

> Done. Branch leave-test-1, 7 commits.
> dotnet test: 18/18 green. tsc: clean.
> Stop-and-ask items: none.
> Orphan fields (in spec.fields but not spec.layout): none.
> Notes for review: (a) HR record table column `note` mapped to nvarchar(max);
>                  (b) on_approve notification body uses 「您的請假」label —
>                      labels.zh-TW.notification.body matched.

If any stop-and-ask items exist, chef lists them with the specific
spec section + the proposed disambiguation. Jason answers, chef
resumes.

## 7. Review + ship (Jason)

```bash
# Diff in GitKraken or:
git log --oneline main..HEAD
git diff main..HEAD

# Local smoke test
cd bpm-svc && dotnet test --filter <CODE>_V<N>
cd ../bpm-ui && npx tsc -p tsconfig.app.json --noEmit
```

When happy, merge into main via GitKraken (chef can't ssh — push +
PR is Jason's job). Since chef wrote against the shared db, no
extra migration step is needed on main after merge.

## Failure modes

- **chef writes outside the allowed paths.** Reject the commit, point
  chef at SKILL §1 rule 1, ask which spec field forced it. Most
  cases are a spec issue, not a chef bug.
- **chef hardcodes a URL.** Same — SKILL §1 rule 4. Convert to
  `${var}` and add the variable to `spec.variables[]` via a wizard
  amendment if it wasn't there.
- **Test fails and chef can't fix without touching read-only code.**
  Stop-and-ask. The fix may be a new bpm-svc primitive, which means
  this flow waits while a separate change lands.
- **chef takes too long / hits Claude turn limit.** Restart the
  session in the same branch, point at git log to show what's
  already committed, continue from there.
- **Two flows in flight at once.** Park one branch, finish the other,
  then switch back. The shared db means you can't have two chef
  sessions migrating concurrently — that's a known trade-off of the
  no-worktree MVP setup.
