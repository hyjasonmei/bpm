# flowcook deployment runbook (Azure, ex-Entra)

Per-customer single-tenant stack. This is the config/secrets/DNS reference
produced in A3; B (Azure provisioning) executes against it. Entra ID / SSO is
deliberately out of scope (auth stays on password login against
`Admin_UserCredentials`).

**`poc.` is the environment prefix, and one env = one Azure Resource Group.**
Subdomains are `poc` / `poc-admin` / `poc-api` / `poc-admin-api`; the whole
stack (Postgres + 2 App Services + 3 static sites + email + Key Vault) lives in
one resource group named for that env (e.g. `rg-poc`). For a real customer you
create a new resource group with their prefix and run the same template — only
DNS + per-service env (CORS origin, frontend API URL, SMTP From) change; no
code changes. This per-RG isolation is the single-tenant model.

## What deploys

| # | Unit | Tech | Domain (POC) | Notes |
|---|------|------|--------------|-------|
| 1 | `bpm-svc` | .NET 10 API | `poc-api.flowcook.ai` | Customer runtime. No Anthropic creds (excluded from this binary). |
| 2 | `bpm-admin-svc` | .NET 10 API | `poc-admin-api.flowcook.ai` | Internal/admin: AI Kitchen, identity, chef MCP. Holds Anthropic key. |
| 3 | `bpm-ui` | React/Vite static | `poc.flowcook.ai` | Calls bpm-svc cross-origin (JWT bearer). |
| 4 | `bpm-admin-ui` | React/Vite static | `poc-admin.flowcook.ai` | Cookie session; calls admin-svc `/api` + bpm-svc `/bpmsvc` — keep **same-origin via a reverse proxy** (see below). |
| 5 | `bpm-www` | Astro static | `flowcook.ai`, `www.flowcook.ai` | Marketing. |
| – | PostgreSQL | Azure DB for PostgreSQL (Flexible Server) | – | Shared by bpm-svc + admin-svc (one DB; two EF history tables). |
| – | Email | Azure Communication Services Email **or** SendGrid (SMTP) | – | bpm-svc SMTP dispatcher points here. |

**chef does NOT deploy** — it's build-time codegen run in your/CI environment
under an `ANTHROPIC_API_KEY` (not a personal subscription). Cooked code ships
inside the bpm-svc/bpm-ui images.

## Secrets & config (env vars per service)

Set via App Service application settings / Key Vault references. Never commit.

### bpm-svc (`poc-api.flowcook.ai`)
| Var | Example | Purpose |
|-----|---------|---------|
| `Database__Provider` | `postgres` | EF provider |
| `ConnectionStrings__Default` | `Host=<pg-host>;Port=5432;Database=flowcook;Username=<u>;Password=<pw>;SSL Mode=Require` | Postgres |
| `BPM_JWT_SECRET` | (≥32 random bytes) | JWT signing |
| `BPM_AUTH_MODE` | `prod` | disables `/api/dev/login` |
| `Cors__BpmUiOrigin` | `https://poc.flowcook.ai` | allowed UI origin(s), comma-sep |
| `Bpm__Notifications__Smtp__Enabled` | `true` | turn on real email |
| `Bpm__Notifications__Smtp__Host` / `Port` / `Username` / `Password` | (ACS or SendGrid SMTP) | relay |
| `Bpm__Notifications__Smtp__Security` | `starttls` | TLS mode |
| `Bpm__Notifications__Smtp__FromAddress` | `no-reply@poc.flowcook.ai` | sender — on the **per-env subdomain** (`@<env>.flowcook.ai`), swapped per customer |
| `Files__RootPath` | (persistent volume path) | uploaded files |

### bpm-admin-svc (`poc-admin-api.flowcook.ai`)
| Var | Example | Purpose |
|-----|---------|---------|
| `Database__Provider` | `postgres` | EF provider |
| `ConnectionStrings__Default` | (same Postgres as bpm-svc) | shared DB |
| `BPM_DB_PROVIDER` | `postgres` | provider for Seeder/design-time callers (env-only) |
| `ANTHROPIC_API_KEY` | (key) | AI Kitchen `/api/chat` + spec-extract |
| `FLOWCOOK_AI_BACKEND` | `api` | use the Anthropic HTTP backend |
| `Bpm__Chef__Token` | (strong token) | chef MCP bearer (dev falls back to `dev-chef-token`) |

### Frontends (build-time, Vite)
| Var | Where | Value |
|-----|-------|-------|
| `VITE_BPM_SVC_URL` | bpm-ui build | `https://poc-api.flowcook.ai` |
| (admin-ui) | — | served behind a reverse proxy that maps `/api` → admin-svc and `/bpmsvc` → bpm-svc, so it stays same-origin (preserves the session cookie, no CORS). Azure Front Door / App Gateway routes, or host admin-ui on admin-svc. |

## CORS / origin model
- **bpm-ui → bpm-svc**: cross-origin, JWT bearer. Set `Cors__BpmUiOrigin=https://poc.flowcook.ai` on bpm-svc.
- **bpm-admin-ui → admin-svc + bpm-svc**: keep **same-origin** via reverse proxy (admin-ui uses cookie auth + the `/api` & `/bpmsvc` path prefixes — these exist only as a Vite dev proxy today; replicate them at the edge in prod). Avoids CORS + keeps the session cookie first-party.

## GoDaddy DNS (add in B, once Azure hostnames exist)
1. **CNAME** per subdomain → its Azure target (App Service / Static Web App / Front Door hostname):
   - `poc` → bpm-ui · `poc-admin` → bpm-admin-ui · `www` → bpm-www
   - `poc-api` → bpm-svc · `poc-admin-api` → bpm-admin-svc
2. **TXT** `asuid.<subdomain>` → the Azure-provided verification id (custom-domain validation + managed TLS).
3. **Apex** `flowcook.ai` → A record to the edge IP, or GoDaddy forwarding to `www` (apex can't be a CNAME).
4. **Email auth** — sending from the **per-env subdomain** (e.g. `poc.flowcook.ai`, swapped per customer). On that subdomain add **SPF** (TXT), **DKIM** (CNAME/TXT from ACS/SendGrid), **DMARC** (TXT). Using a subdomain (not the apex) isolates each env/customer's mail reputation. Without these, mail lands in spam.

Tip: lower TTLs (~600s) on existing records beforehand so cutover is fast.

## Azure resources (B)
- 1× Azure DB for PostgreSQL Flexible Server (B1ms/B2s to start).
- 2× App Service (Linux, .NET 10) — bpm-svc, bpm-admin-svc. Or Container Apps if containerized.
- 3× Static Web Apps (or Storage static site + Front Door) — bpm-ui, bpm-admin-ui, bpm-www.
- Email: ACS Email resource OR SendGrid account (SMTP creds).
- Key Vault — all secrets; App Services reference via managed identity.
- (Optional) Front Door — TLS, apex, and the admin-ui same-origin `/api`+`/bpmsvc` routes.

## Migration / seed order (per environment)
1. admin-svc `ef database update` (creates `Admin_*` identity + admin tables).
2. bpm-svc `ef database update` (feature + bpm tables; Admin_* excluded).
3. Boot admin-svc once → self-seeds identity (or run the admin SeedCli).
4. AI Kitchen → "Register shipped" (or the API) → registers + publishes the cooked flows.

## Build / publish
- APIs: `dotnet publish -c Release` per Api project.
- Frontends: `npm ci && npm run build` (set `VITE_*` first) → upload `dist/`.
