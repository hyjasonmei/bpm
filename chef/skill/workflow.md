# chef MVP workflow

The end-to-end run for a single flow version. Jason drives steps 1–3
manually; chef (Claude session) drives steps 4–6; Jason reviews + ships
in step 7.

## 0. Pre-flight (Jason, once per machine)

```bash
# Where worktrees land — sibling to the main bpm repo
mkdir -p ~/claude/bpm-chef-worktrees

# Where unzipped bundles land
mkdir -p ~/claude/flowcook-bundles
```

## 1. Author + freeze the spec (Jason, in admin wizard)

1. Open AI Kitchen, walk the 11 steps to completion.
2. Download bundle → unzip to
   `~/claude/flowcook-bundles/<FLOWCODE>-v<N>-<YYYYMMDD>/`.
3. Verify the unzipped tree contains `spec.json` + `bpmn.xml` +
   `sampleOrg.json` + `testCases.json` + (optional) `notes/`.

## 2. Spin up a worktree (Jason, in main repo)

```bash
cd ~/claude/bpm
TS=$(date +%Y%m%d-%H%M)
BRANCH="chef/${FLOWCODE}-v${N}-${TS}"
WORKTREE="../bpm-chef-worktrees/${FLOWCODE}-v${N}"

git worktree add -b "$BRANCH" "$WORKTREE"
cd "$WORKTREE"
```

The worktree shares the same `.git` directory; the new branch is local
only — no remote push yet.

## 3. Start chef session (Jason)

```bash
# Inside the worktree
claude
```

In the session, prime chef:

```
Read .claude/skills/chef-codegen/SKILL.md and follow it.
Bundle path: /Users/jason/claude/flowcook-bundles/LEAVE-v1-20260522
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

## 5. Generate (chef)

chef writes files commit-by-commit (see conventions §commit-shape).
After each commit, chef:

- Runs the relevant test (`dotnet test --filter LEAVE_V1` or
  `npx tsc -p tsconfig.app.json --noEmit`).
- Reports a one-liner: "Commit N done, M tests passing".

Failures: chef investigates the test, fixes the implementation (not
the test), commits the fix, re-runs. Never papers over with `[Skip]`.

## 6. Final report (chef)

After the last commit chef tells Jason:

> Done. Branch chef/LEAVE-v1-20260522-1432, 7 commits.
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
cd ~/claude/bpm-chef-worktrees/LEAVE-v1
# Diff in GitKraken or:
git log --oneline main..HEAD
git diff main..HEAD

# Local smoke test
cd bpm-svc && dotnet test --filter LEAVE_V1
cd ../bpm-ui && npx tsc -p tsconfig.app.json --noEmit

# When happy, push via GitKraken (chef can't ssh)
```

After push, Jason opens a PR, optionally runs `/ultrareview <PR#>`,
merges when satisfied, deletes the local worktree:

```bash
cd ~/claude/bpm
git worktree remove ../bpm-chef-worktrees/LEAVE-v1
```

## 8. EF migration (Jason, post-merge)

chef writes the migration class but does NOT run it (per Q5 — keeps
the SQLite db file under `db/` from being mutated by the chef worktree
and visible to the main checkout). After merge, on the main checkout:

```bash
cd bpm-svc
dotnet ef database update
```

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
  session in the same worktree, point at the worktree's git log to
  show what's already committed, continue.
- **Two flows in flight at once.** Use two worktrees + two Claude
  sessions. Branches are independent; only the shared SQLite db
  could cause friction, so don't run `dotnet ef database update` from
  either worktree until both are merged.
