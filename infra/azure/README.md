# flowcook — Azure provisioning (B phase)

Executable companion to `/DEPLOY.md`. DEPLOY.md is the *reference* (what config /
secrets / DNS each service needs); these scripts are the *runbook* that stands a
real environment up with `az` CLI — **no containers**, App Service + Static Web
Apps + Postgres Flexible Server + Key Vault.

One env = one resource group = one prefix. Single-tenant. Entra/SSO out of scope
(password login against `Admin_UserCredentials`).

```
00-config.sh     all env-specific variables (sourced by the others)
01-provision.sh  create resources (RG, Postgres, plan + 2 web apps, 3 SWAs, Key Vault)
02-configure.sh  app settings + Key Vault references + CORS
03-deploy.sh     build/publish + deploy code (admin-svc → bpm-svc → frontends)
```

All scripts are **idempotent** — re-run any of them safely.

## Prerequisites

- Azure CLI, logged in: `az login` then `az account set -s <subscription>`
- `dotnet` 10 SDK, `node`/`npm`
- SWA CLI: `npm i -g @azure/static-web-apps-cli`
- `openssl`, `zip` (preinstalled on macOS/Linux)
- A GoDaddy (or wherever `flowcook.ai` lives) DNS console for the CNAME/TXT records

## Run

```bash
cd infra/azure

# 1. Edit 00-config.sh — set ENV_PREFIX, DOMAIN, LOCATION (SKUs optional).
# 2. Export secrets the scripts must not store in git:
export ANTHROPIC_API_KEY=sk-ant-...        # admin-svc AI Kitchen
# optional SMTP (else email deploys disabled):
export SMTP_ENABLED=true SMTP_HOST=... SMTP_USERNAME=... SMTP_PASSWORD=...

./01-provision.sh     # ~5–10 min (Postgres is the slow part)
./02-configure.sh
./03-deploy.sh
```

Everything sensitive (db password, JWT secret, Anthropic key, chef token, SMTP
password) is generated/stored in **Key Vault**; the web apps read it via
managed identity. Nothing secret is written to git or to plain App Service config.

### New customer / new env

Copy `00-config.sh` → `00-config.acme.sh`, change `ENV_PREFIX` + `DOMAIN`, then
`CONFIG=00-config.acme.sh ./01-provision.sh` (and 02, 03). Fresh resource group,
zero code changes — that's the single-tenant model.

## DNS (after 01, once Azure hostnames exist)

For each custom domain, in your DNS console:

1. **CNAME** → the Azure target:
   - `poc` → bpm-ui SWA · `poc-admin` → admin-ui SWA · `www` → www SWA
   - `poc-api` → bpm-svc app · `poc-admin-api` → admin-svc app
2. **TXT** `asuid.<subdomain>` → the verification id Azure prints during
   custom-domain binding (App Service: `az webapp config hostname add`; SWA:
   `az staticwebapp hostname set`). Managed TLS is issued automatically once verified.
3. **Apex** `flowcook.ai` → A record to the edge, or forward to `www` (apex can't CNAME).
4. **Email auth** on the per-env mail subdomain (`poc.flowcook.ai`): **SPF**,
   **DKIM**, **DMARC** from your ACS/SendGrid console. Without these, mail → spam.

Tip: drop TTLs (~600s) on existing records beforehand so cutover is fast.

## §admin-ui — cross-origin JWT (resolved by unify-jwt)

**No same-origin proxy needed.** admin-ui authenticates with a **JWT bearer**
(not a cookie): admin-svc's `/api/auth/login` mints a token whose claims bpm-svc
also accepts (shared `BPM_JWT_SECRET`, issuer `bpm-svc`, audience `bpm-ui`, and
`sub` = the Admin_Principals user id both services read). admin-ui stores that one
token and sends it as `Authorization: Bearer` to **both** services cross-origin:

- `/api/...` → admin-svc (`VITE_ADMIN_SVC_URL`)
- `/bpmsvc/...` → bpm-svc (`VITE_BPM_SVC_URL`, prefix stripped)

So admin-ui is a **plain static SWA** like bpm-ui / www — no Front Door, no
reverse proxy, no `ENABLE_FRONT_DOOR`. The only requirement is CORS: 02 sets
`Cors:AdminUiOrigin` on admin-svc and adds the admin-ui origin to bpm-svc's
`Cors:BpmUiOrigin`. Verified end-to-end (admin login → cross-origin calls to both
services return 200).

## §.NET 10 on App Service

The scripts default to **framework-dependent** against the built-in
`DOTNETCORE:10.0` stack. If that stack isn't offered in your region yet
(`az webapp list-runtimes --os linux | grep DOTNET`), set
`DOTNET_SELF_CONTAINED=true` in 00-config — 03 then publishes a self-contained
`linux-x64` build and sets the startup command to the native binary, so the
App Service runtime version no longer matters.

## What's automated vs manual

| Automated (scripts) | Manual (you / console) |
|---|---|
| RG, Postgres + db, plan, 2 web apps, 3 SWAs, Key Vault | Custom-domain binding + TXT verification |
| Managed identity + Key Vault RBAC | GoDaddy CNAME/TXT/SPF/DKIM/DMARC |
| All app settings + secret references + CORS | ACS/SendGrid email resource + domain auth |
| Build/publish/deploy all 5 units | — (admin-ui needs no proxy; §admin-ui) |
| DB migration + seed (on app startup) | Register + publish cooked flows in AI Kitchen |

## Smoke test

```bash
curl -sf https://poc-api.flowcook.ai/health && echo OK
curl -s -X POST https://poc-admin-api.flowcook.ai/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"alice@acme.example","password":"flowcook2026"}'
```

(Seeded admin login — change/disable demo credentials before a real customer.)

## Teardown

```bash
source 00-config.sh && az group delete -n "$RG" --yes --no-wait
```
