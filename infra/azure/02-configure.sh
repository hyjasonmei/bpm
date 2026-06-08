#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# 02-configure.sh — app settings, Key Vault references, CORS for both web apps.
# Idempotent: app settings are upserts; secrets are versioned (latest wins).
#
# Secrets (db connection string, JWT secret, Anthropic key, chef token, SMTP
# password) go into Key Vault; the web apps reference them via managed identity
# so nothing sensitive lands in plain App Service config or in git.
#
# Run after 01. Requires ANTHROPIC_API_KEY in env (for admin-svc AI Kitchen).
# Migrations are NOT run here — they apply on app startup (auto-migrate, verified
# locally); deploy admin-svc before bpm-svc in 03 to honour the DEPLOY.md order.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"
az account show >/dev/null 2>&1 || die "Not logged in — run 'az login'."

KV_URI="$(az keyvault show -n "$KV_NAME" -g "$RG" --query properties.vaultUri -o tsv)"
kv_set() { az keyvault secret set --vault-name "$KV_NAME" -n "$1" --value "$2" -o none; }
kv_ref() { printf '@Microsoft.KeyVault(SecretUri=%ssecrets/%s)' "$KV_URI" "$1"; }

# ── Build the Postgres connection string from the password 01 stored ────────
PG_HOST="$(az postgres flexible-server show -n "$PG_NAME" -g "$RG" --query fullyQualifiedDomainName -o tsv)"
PG_PW="$(az keyvault secret show --vault-name "$KV_NAME" -n pg-admin-password --query value -o tsv)"
CONN="Host=${PG_HOST};Port=5432;Database=${PG_DB};Username=${PG_ADMIN_USER};Password=${PG_PW};SSL Mode=Require;Trust Server Certificate=true"
kv_set bpm-connection-string "$CONN"
ok "Connection string → Key Vault (secret 'bpm-connection-string')"

# ── Generate / store the JWT secret (shared by both svcs) ───────────────────
if ! az keyvault secret show --vault-name "$KV_NAME" -n bpm-jwt-secret >/dev/null 2>&1; then
  kv_set bpm-jwt-secret "$(openssl rand -base64 48)"
  ok "Generated JWT secret → Key Vault"
else ok "JWT secret already in Key Vault (reusing)"; fi

# ── Anthropic key + chef token (admin-svc only) ─────────────────────────────
if [ -n "${ANTHROPIC_API_KEY:-}" ]; then
  kv_set anthropic-api-key "$ANTHROPIC_API_KEY"; ok "Anthropic key → Key Vault"
else
  warn "ANTHROPIC_API_KEY not set — AI Kitchen will be non-functional until you set it."
fi
CHEF_TOKEN="${CHEF_TOKEN:-$(openssl rand -hex 24)}"
kv_set chef-token "$CHEF_TOKEN"; ok "Chef MCP token → Key Vault"

# ── SMTP password (optional) ────────────────────────────────────────────────
if [ "$SMTP_ENABLED" = "true" ] && [ -n "$SMTP_PASSWORD" ]; then
  kv_set smtp-password "$SMTP_PASSWORD"; ok "SMTP password → Key Vault"
fi

# Origins allowed to call bpm-svc cross-origin (custom domain + SWA default).
BPM_UI_DEFAULT="$(az staticwebapp show -n "$BPM_UI_SWA" -g "$RG" --query defaultHostname -o tsv 2>/dev/null || true)"
# bpm-svc is called cross-origin by BOTH bpm-ui AND admin-ui: post unify-jwt,
# admin-ui hits bpm-svc (the old /bpmsvc paths) directly with the shared bearer,
# so both UI origins must be allowed.
CORS_ORIGINS="https://${BPM_UI_FQDN},https://${ADMIN_UI_FQDN}${BPM_UI_DEFAULT:+,https://$BPM_UI_DEFAULT}"

# ── bpm-svc app settings ────────────────────────────────────────────────────
say "Configuring $BPM_SVC_APP"
bpm_settings=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "Database__Provider=postgres"
  "ConnectionStrings__Default=$(kv_ref bpm-connection-string)"
  "BPM_JWT_SECRET=$(kv_ref bpm-jwt-secret)"
  "BPM_AUTH_MODE=prod"
  "Cors__BpmUiOrigin=$CORS_ORIGINS"
  "Files__RootPath=/home/data/files"   # /home is the App Service persistent mount
)
# NB: bpm-svc has NO seed env — BPM_SEED_ON_STARTUP is retired (unify-user-store).
# Org/identity data is seeded by admin-svc into the shared db, so deploy admin-svc
# first (03 does this) and bpm-svc reads the same tables.
if [ "$SMTP_ENABLED" = "true" ]; then
  bpm_settings+=(
    "Bpm__Notifications__Smtp__Enabled=true"
    "Bpm__Notifications__Smtp__Host=$SMTP_HOST"
    "Bpm__Notifications__Smtp__Port=$SMTP_PORT"
    "Bpm__Notifications__Smtp__Username=$SMTP_USERNAME"
    "Bpm__Notifications__Smtp__Password=$(kv_ref smtp-password)"
    "Bpm__Notifications__Smtp__Security=$SMTP_SECURITY"
    "Bpm__Notifications__Smtp__FromAddress=$SMTP_FROM"
  )
fi
az webapp config appsettings set -n "$BPM_SVC_APP" -g "$RG" --settings "${bpm_settings[@]}" -o none
ok "$BPM_SVC_APP settings applied"

# ── admin-svc app settings ──────────────────────────────────────────────────
say "Configuring $ADMIN_SVC_APP"
admin_settings=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "Database__Provider=postgres"
  "BPM_DB_PROVIDER=postgres"
  "ConnectionStrings__Default=$(kv_ref bpm-connection-string)"
  "BPM_JWT_SECRET=$(kv_ref bpm-jwt-secret)"
  "FLOWCOOK_AI_BACKEND=api"
  "Bpm__Chef__Token=$(kv_ref chef-token)"
  "FLOWCOOK_ADMIN_SEED_ON_STARTUP=true"
  "Cors__AdminUiOrigin=https://${ADMIN_UI_FQDN}"   # admin-ui calls admin-svc cross-origin w/ JWT bearer
)
[ -n "${ANTHROPIC_API_KEY:-}" ] && admin_settings+=("ANTHROPIC_API_KEY=$(kv_ref anthropic-api-key)")
az webapp config appsettings set -n "$ADMIN_SVC_APP" -g "$RG" --settings "${admin_settings[@]}" -o none
ok "$ADMIN_SVC_APP settings applied"

# Make /home/data/files survive restarts (already persistent on Linux App Service;
# this just ensures the directory is created on first boot via the app).
echo
ok "Configuration complete."
cat <<EOF

Secrets in Key Vault $KV_NAME:
  pg-admin-password · bpm-connection-string · bpm-jwt-secret · chef-token
  $( [ -n "${ANTHROPIC_API_KEY:-}" ] && echo "anthropic-api-key" )$( [ "$SMTP_ENABLED" = "true" ] && echo " · smtp-password" )

CORS origins on bpm-svc: $CORS_ORIGINS

Next: ./03-deploy.sh   (publish + deploy code; admin-svc first, then bpm-svc, then frontends)
EOF
