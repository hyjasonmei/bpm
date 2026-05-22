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

## bpm-svc environment variables

The service reads these at startup. Defaults are tuned for local dev — set
them explicitly when shipping anywhere else.

| Var                     | Default | Notes                                                                                                                          |
|-------------------------|---------|--------------------------------------------------------------------------------------------------------------------------------|
| `BPM_AUTH_MODE`         | `dev`   | `dev` = JWT + `/api/dev/login`; `prod` = JWT only; `disabled` = anonymous (legacy demo).                                       |
| `BPM_JWT_SECRET`        | —       | **Required when auth mode != `disabled`.** HS256 signing key. Must be ≥ 32 bytes — `Program.cs` fails fast otherwise.          |
| `BPM_SEED_ON_STARTUP`   | `true`  | When `true`, runs `OrgFixture` after EF migrations to seed the persona users + roles. Set `false` in prod.                     |
| `BPM_AI_BACKEND`        | `cli`   | `cli` reuses Jason's Claude Code subscription; `api` uses `ANTHROPIC_API_KEY`. Switch to `api` for cloud / service deployments. |
| `Spec__IncomingFolder`  | —       | Where the wizard hand-off drops `spec.json`. Resolved against `ContentRootPath` if relative.                                   |

For local dev a 32-byte hex string is plenty:

```bash
export BPM_JWT_SECRET=$(openssl rand -hex 32)
export BPM_AUTH_MODE=dev
```

---

## Manual instance start (process runtime)

Smoke-test the `add-process-runtime` engine without the UI. All four calls
hit `bpm-svc` directly; assumes the service is already running on
`http://localhost:5290` (the `bpm-ui` default base URL) and `BPM_AUTH_MODE=dev`
so `/api/dev/login` is mounted.

```bash
# 1) Mint a JWT for the "employee" persona (the dev login picks Wilson by
#    default; see appsettings.Development.json → "Personas" for the map).
TOKEN=$(curl -s -X POST http://localhost:5290/api/dev/login \
  -H 'Content-Type: application/json' \
  -d '{"persona":"employee"}' | jq -r .token)

# 2) Start a leave instance from the bundled sample spec.
curl -s -X POST http://localhost:5290/api/processes \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"specCode":"LEAVE","formData":{"days":5,"reason":"family"}}'
# → { "instanceId": "...", "firstTaskId": "..." }

# 3) Switch persona to the manager and list inbox.
TOKEN=$(curl -s -X POST http://localhost:5290/api/dev/login \
  -H 'Content-Type: application/json' \
  -d '{"persona":"manager"}' | jq -r .token)

curl -s "http://localhost:5290/api/tasks/mine?status=open&limit=50" \
  -H "Authorization: Bearer $TOKEN"

# 4) Submit the manager-approval task. Replace TASK_ID with an id from (3).
curl -s -X POST "http://localhost:5290/api/tasks/$TASK_ID/submit" \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"decision":"Approve","comment":"ok"}'
```

Status enums on the wire are **numeric** (server uses default
System.Text.Json — no `JsonStringEnumConverter`). The TypeScript client at
`bpm-ui/src/lib/api/process.ts` normalizes them to string literals; raw
curl users either compare against the integer codes (1=Running,
2=Completed …) or pipe through `jq` for inspection.

For a full walk-through of node order / hooks / append-only history, see
`bpm-svc/CLAUDE.md`.

---

## Notes for new customer onboarding (future)

When `customers/{tenant_code}/` becomes a real workflow (post-Phase A), this
checklist should ship as part of the customer-repo `README.md` so the
onboarding engineer can run it before kicking off Claude Code.
