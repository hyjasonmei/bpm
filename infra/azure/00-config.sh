# shellcheck shell=bash
# ─────────────────────────────────────────────────────────────────────────────
# flowcook Azure provisioning — shared config (B phase, executes against DEPLOY.md)
#
# Every env-specific value lives HERE. The other scripts `source` this file and
# touch nothing customer-specific. To stand up a new customer/env: copy this file
# to 00-config.<env>.sh, change ENV_PREFIX + DOMAIN (+ region/SKU if needed), and
# run 01→02→03 with CONFIG=00-config.<env>.sh.
#
# Model: one env = one resource group = one prefix. Single-tenant, no shared infra.
# Entra/SSO out of scope — auth stays password-login against Admin_UserCredentials.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

# ── Identity of this environment ────────────────────────────────────────────
ENV_PREFIX="${ENV_PREFIX:-poc}"            # subdomain + resource name prefix
DOMAIN="${DOMAIN:-flowcook.ai}"            # apex marketing domain
LOCATION="${LOCATION:-japaneast}"          # Azure region (eastasia=HK is Anthropic-geoblocked; japaneast works)
# Static Web Apps only exist in a few regions (centralus/eastus2/westus2/westeurope/eastasia).
# SWA is a global CDN — its "location" is just metadata, doesn't affect serving or the AI
# (Anthropic is called from the App Service APIs in $LOCATION), so any supported region is fine.
SWA_LOCATION="${SWA_LOCATION:-eastasia}"

# ── Resource group ──────────────────────────────────────────────────────────
RG="${RG:-rg-${ENV_PREFIX}}"

# ── Naming (derived; must be globally unique where noted) ───────────────────
# App Service + Static Web App names must be globally unique → prefix with env.
PLAN_NAME="${PLAN_NAME:-asp-${ENV_PREFIX}-flowcook}"
BPM_SVC_APP="${BPM_SVC_APP:-${ENV_PREFIX}-flowcook-api}"            # bpm-svc
ADMIN_SVC_APP="${ADMIN_SVC_APP:-${ENV_PREFIX}-flowcook-admin-api}"  # bpm-admin-svc
BPM_UI_SWA="${BPM_UI_SWA:-${ENV_PREFIX}-flowcook-ui}"
ADMIN_UI_SWA="${ADMIN_UI_SWA:-${ENV_PREFIX}-flowcook-admin-ui}"
WWW_SWA="${WWW_SWA:-${ENV_PREFIX}-flowcook-www}"
DOCS_SWA="${DOCS_SWA:-${ENV_PREFIX}-flowcook-docs}"
KV_NAME="${KV_NAME:-kv-${ENV_PREFIX}-flowcook}"                     # ≤24 chars, globally unique
PG_NAME="${PG_NAME:-pg-${ENV_PREFIX}-flowcook}"                     # globally unique

# ── Custom domains (per-env subdomains; CNAME these in GoDaddy after 01) ─────
BPM_UI_FQDN="${ENV_PREFIX}.${DOMAIN}"
ADMIN_UI_FQDN="${ENV_PREFIX}-admin.${DOMAIN}"
BPM_API_FQDN="${ENV_PREFIX}-api.${DOMAIN}"
ADMIN_API_FQDN="${ENV_PREFIX}-admin-api.${DOMAIN}"
WWW_FQDN="www.${DOMAIN}"

# ── Option A: Azure-default hostnames (no custom DNS) ───────────────────────
# When true, 02/03 repoint the *_FQDN vars at the Azure-assigned hostnames
# (<app>.azurewebsites.net / <swa>.azurestaticapps.net) discovered after 01, so
# CORS + the frontend API URLs use the real reachable hosts with zero DNS work.
# Flip to false (+ set DOMAIN) later to cut over to custom domains.
USE_DEFAULT_HOSTNAMES="${USE_DEFAULT_HOSTNAMES:-true}"

# ── SKUs (start small; scale later) ─────────────────────────────────────────
PLAN_SKU="${PLAN_SKU:-B1}"                 # Linux App Service plan
PG_SKU="${PG_SKU:-Standard_B1ms}"          # Postgres Flexible burstable
PG_TIER="${PG_TIER:-Burstable}"
PG_VERSION="${PG_VERSION:-16}"
PG_STORAGE_GB="${PG_STORAGE_GB:-32}"

# ── Database ────────────────────────────────────────────────────────────────
PG_DB="${PG_DB:-flowcook}"
PG_ADMIN_USER="${PG_ADMIN_USER:-flowcook}"
# Password: NOT stored here. 01 generates one and writes it to Key Vault.
# Set PG_ADMIN_PASSWORD in your shell to reuse an existing server's password.

# ── .NET runtime on App Service ─────────────────────────────────────────────
# Framework-dependent against the App Service built-in stack. If "DOTNETCORE:10.0"
# is not yet offered in $LOCATION, set DOTNET_SELF_CONTAINED=true → 03 publishes a
# self-contained linux-x64 build and the webapp runs it as a generic binary.
DOTNET_RUNTIME="${DOTNET_RUNTIME:-DOTNETCORE:10.0}"
DOTNET_SELF_CONTAINED="${DOTNET_SELF_CONTAINED:-false}"

# ── Edge / reverse proxy for admin-ui (same-origin requirement) ─────────────
# bpm-admin-ui uses cookie auth + relative /api and /bpmsvc paths (today only a
# Vite dev proxy). In prod those MUST be same-origin. ENABLE_FRONT_DOOR=true makes
# 01 provision Azure Front Door with: admin-ui origin, /api → admin-svc,
# /bpmsvc → bpm-svc. If false, you must replicate the proxy yourself (see README).
ENABLE_FRONT_DOOR="${ENABLE_FRONT_DOOR:-false}"
FD_PROFILE="${FD_PROFILE:-afd-${ENV_PREFIX}-flowcook}"

# ── Email (SMTP relay for bpm-svc notifications) ────────────────────────────
# Provisioning ACS Email + verified domain is multi-step and DNS-gated, so we do
# NOT automate it. Put working SMTP creds (ACS or SendGrid) in these env vars
# before running 02, or leave blank to deploy with email disabled.
SMTP_ENABLED="${SMTP_ENABLED:-false}"
SMTP_HOST="${SMTP_HOST:-}"
SMTP_PORT="${SMTP_PORT:-587}"
SMTP_USERNAME="${SMTP_USERNAME:-}"
SMTP_PASSWORD="${SMTP_PASSWORD:-}"
SMTP_SECURITY="${SMTP_SECURITY:-starttls}"
SMTP_FROM="${SMTP_FROM:-no-reply@${ENV_PREFIX}.${DOMAIN}}"

# ── Anthropic (admin-svc only — AI Kitchen) ─────────────────────────────────
# Set ANTHROPIC_API_KEY in your shell before 02; it goes to Key Vault, never here.
CHEF_TOKEN="${CHEF_TOKEN:-}"               # blank ⇒ 02 generates a strong one

# ── Repo paths (relative to repo root) ──────────────────────────────────────
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BPM_SVC_PROJ="$REPO_ROOT/bpm-svc/src/Api/Api.csproj"
ADMIN_SVC_PROJ="$REPO_ROOT/bpm-admin-svc/src/Bpm.Admin.Api/Bpm.Admin.Api.csproj"
BPM_UI_DIR="$REPO_ROOT/bpm-ui"
ADMIN_UI_DIR="$REPO_ROOT/bpm-admin-ui"
WWW_DIR="$REPO_ROOT/bpm-www"
DOCS_DIR="$REPO_ROOT/bpm-docs"

# ── Helpers ─────────────────────────────────────────────────────────────────
say()  { printf '\033[1;36m▸ %s\033[0m\n' "$*"; }
ok()   { printf '\033[1;32m✓ %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m! %s\033[0m\n' "$*" >&2; }
die()  { printf '\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

# Idempotent guard: run an `az ... show` probe; create only if it fails.
exists() { eval "$1" >/dev/null 2>&1; }

# Option A: repoint the *_FQDN vars at the Azure-assigned hostnames discovered
# after 01. Called at the top of 02 and 03 so CORS + the frontend API URLs use
# the real reachable hosts (no DNS). No-op unless USE_DEFAULT_HOSTNAMES=true.
resolve_azure_hostnames() {
  [ "${USE_DEFAULT_HOSTNAMES:-false}" = "true" ] || return 0
  BPM_API_FQDN="$(az webapp show -n "$BPM_SVC_APP" -g "$RG" --query defaultHostName -o tsv)"
  ADMIN_API_FQDN="$(az webapp show -n "$ADMIN_SVC_APP" -g "$RG" --query defaultHostName -o tsv)"
  BPM_UI_FQDN="$(az staticwebapp show -n "$BPM_UI_SWA" -g "$RG" --query defaultHostname -o tsv)"
  ADMIN_UI_FQDN="$(az staticwebapp show -n "$ADMIN_UI_SWA" -g "$RG" --query defaultHostname -o tsv)"
  WWW_FQDN="$(az staticwebapp show -n "$WWW_SWA" -g "$RG" --query defaultHostname -o tsv)"
  say "Using Azure default hostnames: ui=$BPM_UI_FQDN api=$BPM_API_FQDN admin-ui=$ADMIN_UI_FQDN admin-api=$ADMIN_API_FQDN"
}
