# Claude Code dogfood — required tools

Pre-flight checklist before running `prompt_template_v1.md` against a customer
repo (or a dogfood iteration). Missing any of these and Claude Code will hit
RULE #7 / RULE #8 dead-ends mid-run that cost a turn to discover.

Run the **self-check** block at the bottom; if any line errors, install per
the corresponding section.

---

## Required

### .NET 10 SDK + EF Core CLI
RULE #7 needs `dotnet ef migrations add` to actually emit migration files.
The bpm-svc solution targets net10.0.

```bash
brew install --cask dotnet-sdk         # 10.0+
dotnet tool install --global dotnet-ef # EF Core CLI
# add ~/.dotnet/tools to PATH if not already
```

Known-good versions in this repo: dotnet 10.0.203, dotnet-ef 10.0.7.

### Node 18+ / npm
For `bpm-ui` (`npm install`, `npm run dev`).

```bash
brew install node    # ships current LTS, 22.x at the time of writing
```

Known-good: node 22.20.0, npm 10.9.3.

### Claude Code CLI
The driver. 2.1.x.

```bash
# install per https://claude.com/claude-code (or your team's instructions)
claude --version
```

### chrome-devtools MCP
**This is the one that bites people.** RULE #8 (browser walk-through, the final
acceptance gate) drives Chrome through this MCP — without it, Claude can't take
screenshots, can't click through the SPA, and the whole acceptance section
falls back to "I built it, looks fine" which is exactly what RULE #8 exists to
prevent.

Add to `~/.claude/settings.json` MCP servers (or via `claude mcp add`):

```bash
claude mcp add chrome-devtools npx chrome-devtools-mcp@latest
```

Verify it shows `✓ Connected` in `claude mcp list`. The first invocation in a
session will spawn a controlled Chrome window.

---

## Recommended

- **Google Chrome** installed locally — chrome-devtools MCP attaches to it.
- **`gh` CLI** — only needed if Phase A includes `gh pr create` (RULE #9).
  ```bash
  brew install gh && gh auth login
  ```
- **`jq`** — useful for poking at `spec.json` interactively before handing it
  to Claude.

---

## Self-check

Paste this block; every line should print a version, not an error.

```bash
dotnet --version
dotnet ef --version
node --version
npm --version
claude --version
claude mcp list | grep -E '(chrome-devtools|telegram)' || echo 'WARN: chrome-devtools MCP not registered'
```

If any tool is missing, fix it before starting the dogfood run — discovering
it mid-run wastes a turn and you'll likely have to restart from a clean state
to keep idempotency (RULE #2) honest.

---

## Notes for new customer onboarding (future)

When `customers/{tenant_code}/` becomes a real workflow (post-Phase A), this
checklist should ship as part of the customer-repo `README.md` so the
onboarding engineer can run it before kicking off Claude Code.
