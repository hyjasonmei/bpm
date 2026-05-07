# Design notes

## 1. Why CEL specifically

CEL (Common Expression Language, by Google) is the right choice because:

- **Sandboxed by design** — CEL programs are pure expressions; no I/O, no loops, no recursion, no resource exhaustion vectors. Designed exactly for evaluating user-authored predicates safely.
- **Typed and resource-bounded** — every expression has a static type and bounded execution cost. We can reject expressions that would cost too much.
- **Cross-language** — official implementations in Go / C++ / Java / TypeScript exist. .NET has community implementations (cel-cs, Cel.NET) of varying maturity. The grammar is stable enough that hand-rolled `Sprache`/`Pidgin` is feasible if libraries fall short.
- **Good fit for our use cases** — boolean predicates (gateway/conditional/validator) and value computations (derived) are exactly CEL's sweet spot.
- **Documented spec** — written-down grammar; AI prompts can teach Claude (Stage 1) to emit valid CEL.

Alternatives considered:

- **JavaScript** — too much surface, sandboxing risks, side-effects. We'd be inventing yet another sandbox.
- **JSONLogic** — JSON-shaped, ugly for spec authors to write by hand, weaker type system.
- **Hand-rolled DSL** — what `add-process-runtime`'s minimal evaluator is. Fine for placeholder, eventually buckles under feature pressure.
- **Lua / Python sandbox** — overkill, ecosystem-heavy.

## 2. Library selection criteria

Evaluate the .NET CEL libraries on:

- Compatibility with the CEL spec version we standardize on (v1)
- License (MIT / Apache 2 only)
- Active maintenance (commits in last 6 months)
- AOT-ready (no Reflection.Emit at runtime)
- Test suite quality
- Performance: should evaluate a typical 50-token expression in < 1 ms

If no library passes all bars, we hand-roll the subset we need (~6 weeks of work; prefer to avoid). Library choice is a design call deferred to implementation phase 1.

## 3. ExpressionContext shape

```csharp
public sealed record ExpressionContext(
    JsonElement FormData,
    DateTime NowUtc,
    Guid InitiatorUserId,
    Guid TenantId,
    IReadOnlyDictionary<string, ICelFunction> Helpers);
```

Helpers map CEL function names (`businessDaysBetween`, `sum`, etc.) to delegates that the CEL evaluator invokes. The list is fixed at construction time; spec authors can't add their own.

## 4. Function library — security and determinism

Every function in the library MUST:

- Be deterministic given the same inputs (no `random`, no `Date.now` except via the context's `NowUtc`)
- Be pure (no DB access, no HTTP, no file I/O)
- Be O(n) or better in worst case (no exponential regex, no `O(n²)` joins)
- Have a documented type signature

Examples:

| Function | Signature | Notes |
|---|---|---|
| `now()` | `() -> timestamp` | reads from `ExpressionContext.NowUtc` |
| `businessDaysBetween(start, end)` | `(timestamp, timestamp) -> int` | excludes weekends; calendar integration in later proposal |
| `sum(list)` | `(list<number>) -> number` | reads numeric path from list; CEL syntax `sum(items.amount)` is sugared |
| `count(list)` | `(list<T>) -> int` | length of repeater field |
| `dateAdd(dt, n, unit)` | `(timestamp, int, string) -> timestamp` | unit ∈ allowed set |
| `match(s, pattern)` | `(string, string) -> bool` | regex; pattern compiled with `Timeout = 1s` |

`match()` is the riskiest — regex DoS is real. Mitigations:

- Wrap regex with timeout (1 second cap)
- Reject patterns containing nested quantifiers like `(a+)+`
- Cache compiled regexes per spec snapshot

## 5. Expression validation at spec-load time

When `SpecImportService` parses a new spec, every expression is checked:

```
for each gateway.condition:
  evaluator.Validate(expr, expectedType=Boolean, allowedFields=allTopLevelFormFields, allowedFunctions=libraryFunctions)

for each userTask.fields[i].conditional:
  evaluator.Validate(expr, expectedType=Boolean, allowedFields=fieldIdsInThisUserTask, allowedFunctions=libraryFunctions)

for each userTask.fields[i].validator:
  evaluator.Validate(expr, expectedType=Boolean, allowedFields=fieldIdsInThisUserTask + ['value'], allowedFunctions=libraryFunctions)

for each userTask.fields[i].derivedFrom:
  evaluator.Validate(expr, expectedType=Any, allowedFields=fieldIdsInThisUserTask, allowedFunctions=libraryFunctions)
```

Failures aggregate into a structured error response so the wizard can highlight every broken expression at once, not first-fail.

## 6. Co-existence with MinimalExpressionEvaluator

Strategy:

1. CEL implementation lands; both evaluators registered behind a feature flag `BPM_EXPRESSION_BACKEND=cel|minimal`. Default `minimal` (no behavior change).
2. All existing test fixtures run under `cel` mode and pass.
3. Flip default to `cel` after one release of soak.
4. Remove `MinimalExpressionEvaluator` in the next change after that.

This avoids big-bang risk and gives operations a kill-switch if a customer's existing flow breaks.

## 7. Frontend echo / preview

The wizard's expression input fields gain a small "Validate" button that POSTs the expression to `/api/specs/validate-expression` returning either `{ valid: true, evaluatedSampleResult }` or `{ valid: false, errors }`. The endpoint takes the expression + a sample form-data context; useful for quick author-time sanity check.

## 8. Performance

CEL evaluation is fast (typically < 100 µs per expression on warm JIT). Per task spawn we evaluate 0-2 expressions (gateway condition; possibly derived field). Per form submission we may evaluate a dozen (each field's conditional + validator). No bottleneck expected.

If we ever profile a hot path, the AST per spec snapshot is cacheable — parsed once, reused for every instance using that snapshot. Cache lifetime = spec snapshot lifetime = ProcessInstance lifetime.

## 9. Open questions

- **Inline JavaScript fallback**: should we allow `js: ...` fallback for expressions CEL can't express? Likely no — opens sandbox concerns. Document the escape hatch as "split into multiple steps with a service task in between" rather than embedded code.
- **Custom date format parsing**: CEL has timestamp built-in but customer dates arrive as `"2026-05-08"` strings. Our library's `parseDate(s)` helper handles ISO-8601; more exotic formats (民國年) need a tenant-specific parser registered as a helper function. Defer.
- **Localization of expression errors**: parser errors are English. zh-TW localization adds value if customers self-edit specs; can be a follow-up i18n project.
- **Repeater field path syntax**: CEL uses `items.map(x, x.amount).sum()` semantically; our sugar `sum(items.amount)` is shorter. Decide between maintaining the sugar (more wizard-friendly) vs raw CEL (more CEL-spec-pure). I lean: ship sugar layer in our library, document both syntaxes; spec author can choose.
