# Cel.NET Spike Report

**Decision needed:** does `Cel.NET` (rayokota, NuGet 1.0.0, .NET 8) cover the `bpm-cel-v1` subset well enough that we ship with it, or do we hand-roll a subset using a parser combinator (Sprache)?

**Spike date:** 2026-05-10
**Spiked package:** `Cel.NET` 1.0.0 (published 2025-11-27, 45,900 cumulative downloads, single maintainer rayokota, Apache-2.0)
**Spike harness:** `spikes/cel-net-spike/CelNetSpike` — a console project pulling the NuGet, registering Json.NET registry, and exercising 30 expression cases representative of `bpm-cel-v1`.

## Result

**30 pass / 0 fail.** Cel.NET evaluates every operator, literal, comparison, list, string built-in, and macro we tested out of the box. Real-world spec expressions from the BPM domain (`leave_type == '病假'`, `amount > 50000`, `quantity * unit_price`, `email.matches(...)`, `size(reason) >= 10`) all evaluate correctly with no surprises.

## Coverage breakdown vs `bpm-cel-v1` subset

| Feature | bpm-cel-v1 status | Cel.NET coverage | Notes |
|---|---|---|---|
| `==`, `!=`, `<`, `<=`, `>`, `>=` | required | ✅ native | Verified across string / int / double |
| `&&`, `\|\|`, `!` | required | ✅ native | |
| `+`, `-`, `*`, `/`, `%` | required | ✅ native | |
| Division by zero throws | required (no Inf/NaN) | ✅ native | `/0` raises exception, our parity rule satisfied |
| Ternary `?:` | required | ✅ native | |
| `in [list]` | required | ✅ native | |
| Field access `a.b.c` | required | ✅ native | Verified via `JsonRegistry` + `Decls.NewObjectType(typeof(SubmitterDto).FullName)` |
| String literals | required | ✅ native | Both `'…'` and `"…"` |
| Int / decimal / bool / null literals | required | ✅ native | |
| List literals `[1, 2, 3]` | required | ✅ native | |
| `size(x)` (= our `len`) | required as `len` | ✅ native — needs alias mapping | Document `len` as v1's spec-author-facing name; backend maps to `size` |
| `string.matches(regex)` (= our `match`) | required as `match` | ✅ native — needs alias mapping | Same as above |
| `string.contains` / `startsWith` / `endsWith` | required as `contains` | ✅ native | Note: CEL's `contains` is method-style (`s.contains(x)`), not function-style (`contains(s, x)`). Pick one for v1 spec; current draft uses function form — need to either alias or change spec to method form |
| `exists` / `filter` / `map` macros | EXCLUDED in v1 | ✅ present in CEL | We need a **validator pass** to reject these in spec author input (CEL itself accepts them) — this is by design |
| Object property access | required | ✅ native | Json.NET registry handles plain C# DTOs without protobuf |

## Custom functions still needed

These 7 are NOT in CEL standard library — must be registered as custom functions:

| Function | Difficulty | Notes |
|---|---|---|
| `sum(list)` | trivial | C# helper, register on the `ScriptHost` |
| `lower(s)`, `upper(s)` | trivial | Same |
| `now()`, `today()` | trivial | Wraps `IClock.UtcNow` (sandbox-aware via SandboxClock decorator) |
| `daysBetween(t1, t2)` | trivial | C# `(t2 - t1).Days` |
| `businessDaysBetween(t1, t2)` | medium | Depends on `add-calendar-and-business-hours` (calendar lookup); stub returning natural days while calendar capability is unimplemented |

Custom function registration in Cel.NET is documented (`Decls.NewFunction` + script-builder declarations) — not exotic, just work. None of these require fork-level patching.

## Cross-runtime parity (JS side)

Cel.NET is .NET only. The frontend still needs an independent JS evaluator implementing the same subset. Options:

- **`cel-js`** (third-party JS CEL impl) — quick check needed: NPM presence, maintenance, feature coverage. NOT investigated in this spike (out of scope per Jason's "spike Cel.NET" framing).
- **Hand-rolled JS subset** — limited to bpm-cel-v1 surface only; ~1 week work using a parser combinator like `parsimmon`.

The `bpm-cel-v1` subset spec already documents cross-runtime parity rules (UTF-8 string ordering, `decimal.js` for decimals, ECMAScript regex flavor, strict null/bool, throw-on-div-by-zero) — these need to be enforced regardless of which JS library we choose. The contract is the spec, not the implementation.

## Risks observed

1. **Single-maintainer bus factor**: GitHub repo has 8 stars, last release Nov 2025, only `rayokota` commits. If the project goes unmaintained we'd fork or replace — but that's a year-out concern, and CEL spec stability is high (Google governance) so a fork would mostly track.
2. **NuGet warning NU1904**: transitive dep `System.Drawing.Common 4.7.0` has a known critical CVE. Spike build emits 2 warnings about it. Production use needs either (a) ignore (we don't use System.Drawing in our code path), (b) override transitive dep version, or (c) pressure rayokota to update.
3. **Method-style vs function-style built-ins**: CEL standard library prefers method-style (`s.contains(x)`, `s.matches(regex)`). Our spec draft uses function-style (`contains(s, x)`, `match(s, regex)`). Pick one and align the spec — recommendation: align with CEL native to avoid alias overhead.

## Recommendation

**GO with Cel.NET.** The spike showed zero behavioral surprises, every operator we need works, custom functions are routine to register, and the alternative (hand-rolling 4-6 weeks of parser + evaluator + edge-case work) is only justified if Cel.NET demonstrably falls down — it didn't.

Concrete follow-up tasks before adopting in `bpm-svc`:

1. Add `Cel.NET 1.0.0` to `bpm-svc/src/Application/Application.csproj`
2. Implement `IExpressionEvaluator` (in `add-cel-expressions` proposal) wrapping `ScriptHost`
3. Register the 7 custom functions (`sum`, `lower`, `upper`, `now`, `today`, `daysBetween`, `businessDaysBetween`) — backed by `IClock` for time, `IBusinessCalendarService` for business days
4. Build a `BpmCelV1Validator` AST-walker that rejects:
   - Macro use (`exists` / `filter` / `map` / `all`)
   - Bitwise operators
   - Map literals `{}`
   - Slice syntax `a[1:3]`
5. **Decide method-style vs function-style for `contains` / `matches` / etc.** — recommendation: method-style to match CEL native, update `bpm-cel-v1` spec accordingly
6. Override `System.Drawing.Common` transitive dep to a non-vulnerable version
7. JS-side: separate spike for `cel-js` or commit to hand-rolled subset (out of scope here)

## Spike artefacts

- `spikes/cel-net-spike/CelNetSpike/Program.cs` — 30 test cases, runnable via `dotnet run --project spikes/cel-net-spike/CelNetSpike`
- `spikes/cel-net-spike/CelNetSpike/CelNetSpike.csproj` — minimal project, sole NuGet ref `Cel.NET 1.0.0`

To re-run after an update:

```
cd /Users/jason/claude/bpm
dotnet run --project spikes/cel-net-spike/CelNetSpike
```

Expect `30 pass / 0 fail`.
