# Sample Specs

Hand-written `spec.json` files used for dogfood (Phase A) and Pipeline tests (Phase B).

## Files

- `leave_v1.json` — 請假流程 spec deliverable v1.0 (corresponds to `spec_schema.md` §3 example)

## Dogfood guide (Phase A)

When you're ready to dogfood the prompt template against `bpm-svc`:

```bash
# 1. From bpm/, copy the scaffold into a customers/ workdir for "acme"
mkdir -p customers/acme
cp -R bpm-svc/* customers/acme/
cd customers/acme

# 2. git init the customer repo (so Claude Code can do git ops)
git init && git add -A && git commit -m "scaffold from bpm-svc"

# 3. Drop the spec into the repo root
cp ../../sample_specs/leave_v1.json ./spec.json

# 4. Run Claude Code with the prompt template as system prompt
claude --append-system-prompt "$(cat ../../prompt_template_v1.md | sed -n '/^## Complete Prompt Template/,/^---$/p')" \
       -p "Read spec.json from this repo root and generate the workflow code per the system prompt. Stop before opening a PR — leave staged changes only."

# 5. Inspect the diff manually
git status && git diff --staged

# 6. Try to build
export DOTNET_ROLL_FORWARD=LatestMajor
dotnet build

# 7. Note findings and feed back into prompt_template_v1.md (v1.1 / v1.2)
```

If `dotnet build` fails on the very first run, that's OK — that's exactly the
information we need to refine the prompt template. Append observations to
`../prompt_template_v1.md` under the "Prompt 演化紀錄" section.

## Why hand-written

Phase A: there's no 9-step UI yet that exports `spec.json`. We hand-write a
representative spec to validate the Claude Code → C# pipeline works before we
invest in the UI. Once the UI ships, the spec exporter takes over and these
hand-written specs become regression test fixtures.

## Adding more samples

When dogfooding more flow types, add them here with version suffix:
- `purchase_v1.json` — 採購流程
- `announcement_v1.json` — 發公告流程
- `travel_v1.json` — 差旅申請

Keep each as a single self-contained file. No partial / templated specs.
