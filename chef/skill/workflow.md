# chef MVP workflow

End-to-end run for a single flow version. The operator drives steps 0-3
manually; chef (Claude session) drives steps 4-7 inside a normal
checkout on a fresh testbed branch; the operator reviews + ships in step 8.

There is **no separate git worktree** in this MVP. chef runs against
the same repo the operator works in, on a per-flow branch (e.g.
`leave-test-1`), and chef + main bpm stack share the same `db/bpm.db`.

**Only one dev-server stack at a time** on the local box (they share
ports 5290 / 5173).

## 0. Pre-flight (the operator)

Per chef session — shut any running dev stack down before chef boots:

```bash
lsof -ti :5290 :5173 | xargs -r kill
```

## 1. Author + freeze the spec (the operator, in admin wizard)

1. Open AI Kitchen, walk the 11 steps to completion.
2. Download bundle → unzip to
   `~/claude/flowcook-bundles/<FLOWCODE>-v<N>-<YYYYMMDD>/`.
3. Verify the unzipped tree contains `spec.json` + `bpmn.xml` +
   `manifest.json` + `forms/` + `sample-org.json` + `test-cases/`.

## 2. Create / reset the chef branch (the operator)

Testbed branches are mutable — reset and re-cook freely:

```bash
cd ~/claude/bpm
git checkout main
git pull
BRANCH="<flowcode>-test-1"    # or test-2, test-3, …
git checkout -B "$BRANCH"     # -B resets if it already exists
```

Never merge a testbed branch wholesale back to main — main lands via
clean cherry-picks once chef + lead are happy.

## 3. Start the chef session (the operator)

```bash
cd ~/claude/bpm
claude
```

In the session, prime chef:

```
Read chef/skill/SKILL.md and follow it.
Bundle path: ~/claude/flowcook-bundles/LEAVE-v1-20260522
```

That's it.

## 4. Read + plan (chef)

chef reads in the order spelled out in SKILL.md §4. After reading,
chef states a one-paragraph plan back to the operator:

> Plan for LEAVE V1: entity with Status enum (PendingManager /
> PendingVp / PendingHr / Completed / Rejected / Cancelled) + per-stage
> approver columns; state-machine service with Submit / ManagerDecision
> (gateway: days>=7 routes to VP, else to HR) / VpDecision / HrArchive;
> 7-endpoint controller; 2 notification templates; React form (dual
> date inputs for the daterange, JS-computed days, conditional cert
> FilePicker); ITypedInboxProvider; ~15 unit tests + chrome E2E.
> Stop-and-ask: (a) notify_assign_manager body has trailing
> {{undefined-token}} stretch — bake or ignore? (b) approval_vp uses
> natural-language fallback "走 VP 角色" — bake as role:VP fallback?

The operator accepts or pushes back. Once accepted, chef starts writing.

## 5. Generate + verify (chef)

### 5a. Generate

chef writes files commit-by-commit (see conventions §commit-shape).
After each commit, chef:

- Runs the relevant test (`dotnet test --filter <CODE>_V<N>` or
  `npx tsc -p tsconfig.app.json --noEmit`).
- Reports a one-liner: "Commit N done, M tests passing".

Failures: chef investigates and fixes the implementation (not the
test). Never papers over with `[Skip]`.

### 5b. Migration

`DbPathResolver` lands the SQLite db at `<repoRoot>/db/bpm.db`. chef
runs the migration tool from `bpm-svc`:

```bash
cd bpm-svc
dotnet ef migrations add <CODE>_V<N>_InitialCreate \
    --project src/Persistence --startup-project src/Api
dotnet ef database update \
    --project src/Persistence --startup-project src/Api
```

Identity / org seed is owned by admin-svc — boot admin-svc once on
a fresh db and it self-seeds. Don't write user / dept / role rows
from chef.

### 5c. Boot dev servers + chrome click-through

```bash
# Terminal A — bpm-svc
cd bpm-svc
ASPNETCORE_ENVIRONMENT=Development \
BPM_AUTH_MODE=dev \
BPM_JWT_SECRET=dev-jwt-secret-needs-to-be-at-least-32-bytes-long-yes \
dotnet run --project src/Api

# Terminal B — bpm-ui
cd bpm-ui
npm run dev
```

chef opens chrome-devtools and exercises **the full submitter +
approver loop**:

1. Login as the spec's submitter persona (LEAVE → `bob@acme.example`).
2. Navigate `/apply/<CODE>` — open the "View BPMN" modal and confirm
   it renders the **bundle diagram** (gateway diamonds, exact labels
   admin sees), not the linear fallback. Powered-by-bpmn.io footer
   visible.
3. Fill the form, submit. The form should redirect to Home.
4. Verify the new case shows under "My Recent Cases" on Home.
5. Click the row → land on the case-detail page. Confirm: header /
   field grid / 簽核 timeline render; "View BPMN" button opens the
   modal with the **current** spec node highlighted amber and any
   prior nodes green. No approve / reject buttons visible (detail is
   view-only).
6. Drive every state-machine transition via the per-flow REST
   endpoints (curl + a JWT minted via `/api/auth/login`), and reopen
   "View BPMN" after each transition. **Every spec node ID that the
   case walks must light up green at the right step, and the new
   current node must light amber.** Skipped branches (gateway
   `false` paths) stay uncoloured. The Completed terminal must paint
   the end event green.
7. Clear the JWT and log in as the next-step approver (LEAVE →
   `alice@acme.example` for the manager step).
8. Verify the case shows under "Pending My Approval".

A row in the DB is not enough — invisible cases fail the demo, and
a stage-by-stage BPMN check that doesn't light up every node fails
demo too.

## 6. Final report (chef)

After the last commit chef tells the operator:

> Done. Branch leave-test-4, 5 commits.
> dotnet test --filter LEAVE_V1: 15/15 green. tsc: clean.
> Chrome E2E: Bob submits → Bob's Home renders the row → Alice sees
> "Bob 申請 特休 3 天" in Pending My Approval. Screenshot attached.
> Stop-and-ask items resolved: (a) notify body trailing tokens ignored
> per the operator; (b) approval_vp baked as `dept_head → role:VP` fallback.
> Orphan fields: none.
> Spec ⇄ sampleOrg drift: spec.access.launchableBy carries
> `dept:f745…` not in sampleOrg — test path uses seeded dept instead.

If any stop-and-ask items remain open, chef lists them with the
specific spec section + the proposed disambiguation.

## 7. Review + ship (the operator)

```bash
git log --oneline main..HEAD
git diff main..HEAD
```

Local smoke:

```bash
cd bpm-svc && dotnet test --filter <CODE>_V<N>
cd ../bpm-ui && npx tsc -p tsconfig.app.json --noEmit
```

Manual chrome run on the live dev server (the chef session left it
running).

When happy, cherry-pick the chef commits into main via GitKraken. The
testbed branch can stay around for the next iteration or be reset.

## Failure modes

- **chef writes outside the allowed paths.** Reject the commit, point
  chef at SKILL §1 rule 1, ask which spec field forced it. Most cases
  are a spec issue, not a chef bug.
- **chef hardcodes a URL.** Same — SKILL §1 rule 3. Convert to
  `${var}` and add the variable to `spec.variables[]` via a wizard
  amendment if it wasn't there.
- **Test fails and chef can't fix without touching read-only code.**
  Stop-and-ask. The fix may be a new lead-side primitive.
- **Case lands in the DB but Home stays empty.** Chef forgot the
  `ITypedInboxProvider`, or shipped it but the inbox controller
  isn't surfacing it. Check the `ITypedInboxProvider` assembly scan
  in `Application/DependencyInjection.cs` (target) — and the legacy
  one in `Persistence/DependencyInjection.cs` if it's still there —
  for whether the scan covers the assembly your provider lives in.
  If chef put the provider in `Application/Features/<CODE>/V<N>/`
  but only Persistence is being scanned, the runtime silently drops
  it. Lead fixes the scan; chef checks the provider's `FlowCode`
  returns the right value.
- **chef takes too long / hits Claude turn limit.** Restart the
  session in the same branch, point at git log to show what's already
  committed, continue from there.
- **Two flows in flight at once.** Park one branch, finish the other,
  then switch back. The shared db means you can't have two chef
  sessions migrating concurrently — known trade-off of the
  no-worktree MVP setup.
