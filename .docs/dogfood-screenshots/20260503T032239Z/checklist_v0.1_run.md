# review_checklist.md v0.1 — first run on dogfood-purchase

> Purpose: take the checklist we just wrote and walk it once against
> generated code (commits `59fb0ae` + `13cafd7` on `dogfood-purchase`).
> Find: false positives (would flag clean code), false negatives (would miss
> real problems), missing checks, stale wording.
> Outcome: feed v0.2 of `review_checklist.md`.

---

## Method

For each ⚙ / 👁 / 🌐 item in `review_checklist.md` v0.1 (~30 checks):
- run the proposed verification
- record: `PASS` / `FAIL` / `N/A` / `weak`
- `weak` = the check passed but the wording would let a buggy variant through

---

## Results by section

### 1. Spec faithfulness — RULE #1

| # | Check | Result | Note |
|---|---|---|---|
| 1.1 | flowCode → file naming | PASS | found `Purchase{Workflow,Case,State,Events,Controller}.cs` |
| 1.2 | userTasks fields → entity properties | PASS, weak | `quote_file` → `QuoteFileName` (not `QuoteFile`). v0.1 says "命名可以是 PascalCase 轉換" but doesn't mention common suffixes like `…FileName` for `…_file`. Buggy variant: a generator emitting `QuoteFile` (string) would also pass v0.1's check but be inconsistent with the leave precedent. **Add to v0.2: filename convention.** |
| 1.3 | approvals → Resolver methods | PASS | direct_manager / role:Finance / role:CEO+VP fallback all present |
| 1.4 | decisions → Evaluator | PASS | both gateway thresholds (10k / 100k) implemented exactly; isDefault edges are e4 / e7 as in spec |
| 1.5 | notifications → Emitter | PASS, weak | All 3 notifications emitted at correct triggers. **Weakness:** v0.1 says "每個 trigger 都被呼叫" but doesn't check that templates' `variables[]` array matches the keys actually fed to MustacheLite. Easy to drift. **Add to v0.2.** |
| 1.6 | validator/conditional rules | PASS | Amount range matches spec.validator; quote_file `.When(x.Amount >= 10000)` matches conditional; error message verbatim from spec.testCases[3].expectedValidationErrors |
| 1.7 | Bilingual labels | PASS | every Field has `label="zh / en"` pattern |
| 1.8 | role permission enforced | PASS | ExecuteHandler checks `Roles.Contains("Purchase")`, throws ConflictException |

### 2. Idempotency — RULE #2/#3

| # | Check | Result | Note |
|---|---|---|---|
| 2.1 | No tenant in business logic | PASS | grep returned 0 hits in Domain/Application |
| 2.2 | Imports / case sorted | PASS, weak | Looks consistent but v0.1 says "兩次跑同 spec 產出 diff 為空"—we never actually ran it twice. **Cannot fully verify w/o re-running**; reword as "spot-check" until we have automation. |

### 3. End-to-end reachable — RULE #6

| # | Check | Result | Note |
|---|---|---|---|
| 3.1 | New flow in FORM_GROUPS | PASS | found in `Purchase` group |
| 3.2 | Label is human-readable | PASS | `Purchase Request (採購申請)`, no dev marker |
| 3.3 | Hash deep-link | PASS | App.tsx has read on init + hashchange listener + state-to-hash sync |
| 3.4 | App.tsx switch handles new code | PASS | `case 'PURCHASE': body = caseId ? View : Form` |
| 3.5 | workflow.ts FORMS entry | PASS | FormCode union includes PURCHASE; FORMS has steps + ownerByStep |

### 4. Migrations — RULE #7

| # | Check | Result | Note |
|---|---|---|---|
| 4.1 | Three migration files committed | PASS | `20260503025511_AddPurchase.cs`, `.Designer.cs`, `AppDbContextModelSnapshot.cs` all present in git |
| 4.2 | DbSet in both contexts | PASS | `AppDbContext` + `IAppDbContext` both have `DbSet<PurchaseCase> PurchaseCases` |
| 4.3 | db update succeeds | PASS | re-ran during walk-through; SQLite has table |

### 5. Browser walk-through — RULE #8

| # | Check | Result | Note |
|---|---|---|---|
| 5.1–5.4 | a/b/c/d preflight | PASS | all green |
| 5.5 | step e (no URL typed) | PASS | navigation via Create dropdown only |
| 5.6 | step f (controlled inputs) | PASS, weak | tc_1 walk used the v1.2 native value setter trick. **tc_3 follow-up** found two new gotchas not in v0.1 yet: (a) `<input type=file>` needs DataTransfer + new File; (b) chrome-devtools `fill` on `<select>` matches **display label**, not `<option value>`. **Add both to v0.2 §5.6.** |
| 5.7 | step g list | PASS | curl /api/purchase/cases returned the case |
| 5.8 | step h DOM has all fields | PASS | a11y snapshot shows Vendor/Category/Amount/Items/Justification/QuoteFileName/PoNumber/ExpectedDelivery |
| 5.9 | screenshot dir present | PASS | `dogfood-screenshots/20260503T032239Z/` |
| 5.10 | per-screenshot assertions | PASS | ASSERTIONS.md has one trace-back per screenshot |
| 5.11 | step k servers killed | PASS | post-walk-through: lsof on 5290 / 5173 empty |
| 5.12 | 0 console errors / 0 4xx 5xx | PASS | confirmed twice (tc_1 + tc_3) |

### 6. DI registration

| # | Check | Result | Note |
|---|---|---|---|
| 6.1 | Resolver/Emitter AddScoped | PASS | both in Application.DependencyInjection (and yes, this round we tripped on it once mid-flight; v0.1 capturing it is correct) |
| 6.2 | Persistence DI three pieces | PASS | IAppDbContext / IIdentityProvider / INotificationSender all registered |

### 7. Tests

| # | Check | Result | Note |
|---|---|---|---|
| 7.1 | dotnet test all green | PASS | 26 tests pass |
| 7.2 | spec.testCases full coverage | PASS, weak | `Tc1_…`, `Tc2_…`, `Tc3_…`, `Tc4_…` (×2 for boundary) all present, but v0.1 says "DisplayName references testCase id"—the actual code uses `[Fact(DisplayName = "tc_2: …")]` strings rather than checking against spec.testCases[].id mechanically. Buggy variant: generator skips a testCase silently and humans don't notice. **v0.2: machine-check that count of `Tc{N}_` test methods ≥ count of `spec.testCases[]`.** |
| 7.3 | Unit tests for Resolver/Evaluator | PASS | 7 + 11 (parametric) tests |

---

## False positives (v0.1 flags but code is fine)

None found in this run. Wording in §2.2 (imports/case sorted) is conservative
enough that it doesn't false-alarm but also isn't strict enough to actually
catch drift — see §2.2 weakness above.

---

## False negatives (v0.1 would miss real bugs)

These are issues that the actual code happens to handle, but a buggy
generator could break, and v0.1 wouldn't catch:

1. **Vite proxy `/api → :5290` not in checklist.** If a generator forgot
   this, the form would 404 in dev (api requests go to :5173). v0.1 missed
   it; we're lucky the dogfood prompt got it right. **Add to v0.2 §3.**

2. **`Api.csproj` `<None Update="identity-acme.csv"><CopyToOutputDirectory>`
   not checked.** Without it, the CSV doesn't ship to bin/, and
   CsvIdentityProvider throws FileNotFoundException at runtime — but only
   at startup, not at compile. This bit us in round 1 (per the leave
   commit history). **Add to v0.2 §4.**

3. **`appsettings.json` `Identity:CsvPath` key not checked.** Same class as
   above; without the config, the DI fallback to `"identity-acme.csv"`
   relative path would still work, but a generator might expect the config
   to be present. **Add to v0.2 §4.**

4. **`Application.csproj` MUST reference `Microsoft.EntityFrameworkCore`**
   because `IAppDbContext` lives in Application and uses `DbSet<>`. Round 1
   caught this; v0.1 doesn't check it. **Add to v0.2 §6.**

5. **Notification template `variables[]` array vs MustacheLite dictionary
   key drift** — see §1.5 above.

6. **No check that the route prefix in `[Route("api/{flow}")]` matches
   `flowCode.toLower()`.** A generator could emit `[Route("api/purchasing")]`
   while frontend expects `/api/purchase/...` and 404s would only show at
   runtime. Quick grep check possible. **Add to v0.2 §3.**

7. **Persona mapping in `purchaseApi.ts personaToActingUserId` not
   checked.** This is the multi-tier-gate trick from the LEAVE_SPEC port.
   v0.1 doesn't verify it exists. Multi-tier flows that skip this trick
   make the UI unusable for state ≥ 3. **Add to v0.2 §3.**

---

## Missing checks → v0.2 candidates

Aggregating from the section-by-section notes, here are the proposed
additions for v0.2:

- §1.2: extend property naming guidance (file → FileName, daterange → StartDate/EndDate, etc.)
- §1.5: variables[] vs MustacheLite key set (set equality)
- §3.6 (new): Vite proxy `/api → :5290` exists
- §3.7 (new): personaToActingUserId trick present when ≥2 approval nodes
- §3.8 (new): Controller route prefix matches flowCode.toLower()
- §4.4 (new): Api.csproj has CopyToOutputDirectory for identity-*.csv
- §4.5 (new): appsettings.json has Identity.CsvPath key
- §5.6 (extend): file inputs need DataTransfer; select fill needs display label
- §6.3 (new): Application.csproj references Microsoft.EntityFrameworkCore
- §7.2 (extend): mechanical test-method-count ≥ spec.testCases length

---

## Net assessment

- **30 v0.1 checks ran.** ~75% PASS, ~25% PASS-but-weak, 0 FAIL, 0 false positive.
- **7 false negatives identified** (gaps in coverage).
- v0.1 is **directionally correct but porous**. As a Phase B Review Agent
  prompt source it would catch the big classes (spec faithfulness,
  migration completeness, e2e reachability) but would let infrastructure /
  config drift through.
- v0.2 should pick up the 7 gaps and tighten the 8 weak items.
- Recommend writing v0.2 immediately while details are fresh, rather than
  waiting another dogfood round.

---

## v0.2 priority

If we only do top-3 changes:
1. Add §4.4 Api.csproj CopyToOutputDirectory check (high impact: would
   have caught a real round-1 bug)
2. Add §3.6 Vite proxy check (high impact: silent dev-time 404)
3. Add §6.3 Application.csproj EFC reference check (high impact: round-3
   would have caught the missing reference earlier)

The other 4 are lower-impact but cheap to add.
