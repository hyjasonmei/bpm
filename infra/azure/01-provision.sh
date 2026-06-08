#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# 01-provision.sh — create the Azure resources for one flowcook env.
# Idempotent: re-running skips anything that already exists.
#
# Creates: resource group · Postgres Flexible Server + db · App Service plan +
# 2 web apps (managed identity) · 3 Static Web Apps · Key Vault (+ grant the web
# apps read access) · optional Front Door. Does NOT push code or settings — that
# is 02 (configure) and 03 (deploy).
#
# Prereqs: az CLI logged in (`az login`), correct subscription selected
#   (`az account set -s <sub>`). Run from anywhere; pass CONFIG to override.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"

command -v az >/dev/null || die "az CLI not found — install Azure CLI first."
az account show >/dev/null 2>&1 || die "Not logged in — run 'az login' (and 'az account set -s <sub>')."
SUB="$(az account show --query name -o tsv)"
say "Subscription: $SUB   ·   Env: $ENV_PREFIX   ·   RG: $RG   ·   Region: $LOCATION"

# ── Resource providers (fresh subscriptions aren't registered for these) ────
# --wait blocks until each provider reaches Registered. No-op once registered.
say "Registering resource providers (one-time on a fresh subscription)…"
for ns in Microsoft.KeyVault Microsoft.Web Microsoft.DBforPostgreSQL Microsoft.Storage Microsoft.Network; do
  az provider register --namespace "$ns" --wait -o none 2>/dev/null \
    && ok "provider $ns registered" \
    || warn "provider $ns register skipped/failed"
done

# ── Resource group ──────────────────────────────────────────────────────────
if exists "az group show -n '$RG'"; then ok "RG $RG exists"; else
  say "Creating RG $RG"; az group create -n "$RG" -l "$LOCATION" -o none; ok "RG $RG"
fi

# ── Key Vault (created early so the db password can land in it) ──────────────
if exists "az keyvault show -n '$KV_NAME' -g '$RG'"; then ok "Key Vault $KV_NAME exists"; else
  say "Creating Key Vault $KV_NAME (RBAC auth)"
  az keyvault create -n "$KV_NAME" -g "$RG" -l "$LOCATION" \
    --enable-rbac-authorization true -o none
  ok "Key Vault $KV_NAME"
fi
KV_ID="$(az keyvault show -n "$KV_NAME" -g "$RG" --query id -o tsv)"
ME="$(az ad signed-in-user show --query id -o tsv 2>/dev/null || true)"
if [ -n "$ME" ]; then
  # Let the operator running this script write secrets.
  az role assignment create --assignee "$ME" --role "Key Vault Secrets Officer" \
    --scope "$KV_ID" -o none 2>/dev/null || true
fi

# ── Postgres Flexible Server + database ─────────────────────────────────────
if exists "az postgres flexible-server show -n '$PG_NAME' -g '$RG'"; then
  ok "Postgres $PG_NAME exists"
else
  PG_ADMIN_PASSWORD="${PG_ADMIN_PASSWORD:-$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-24)}"
  say "Creating Postgres Flexible Server $PG_NAME ($PG_TIER/$PG_SKU, v$PG_VERSION)"
  az postgres flexible-server create \
    -n "$PG_NAME" -g "$RG" -l "$LOCATION" \
    --tier "$PG_TIER" --sku-name "$PG_SKU" \
    --version "$PG_VERSION" --storage-size "$PG_STORAGE_GB" \
    --admin-user "$PG_ADMIN_USER" --admin-password "$PG_ADMIN_PASSWORD" \
    --database-name "$PG_DB" \
    --public-access 0.0.0.0 \
    --yes -o none
  az keyvault secret set --vault-name "$KV_NAME" -n pg-admin-password \
    --value "$PG_ADMIN_PASSWORD" -o none
  ok "Postgres $PG_NAME  (password → Key Vault secret 'pg-admin-password')"
  warn "Public access opened to all Azure services (0.0.0.0). Tighten to App Service outbound IPs / VNet before real customer data."
fi
PG_HOST="$(az postgres flexible-server show -n "$PG_NAME" -g "$RG" --query fullyQualifiedDomainName -o tsv)"
# Allow Azure-internal services (App Service) to reach the server.
az postgres flexible-server firewall-rule create -n "$PG_NAME" -g "$RG" \
  --rule-name AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none 2>/dev/null || true

# ── App Service plan (Linux) ────────────────────────────────────────────────
if exists "az appservice plan show -n '$PLAN_NAME' -g '$RG'"; then ok "Plan $PLAN_NAME exists"; else
  say "Creating Linux App Service plan $PLAN_NAME ($PLAN_SKU)"
  az appservice plan create -n "$PLAN_NAME" -g "$RG" --is-linux --sku "$PLAN_SKU" -o none
  ok "Plan $PLAN_NAME"
fi

# ── Two web apps (bpm-svc, admin-svc) with system-assigned identity ─────────
create_webapp() {
  local app="$1"
  if exists "az webapp show -n '$app' -g '$RG'"; then ok "Web app $app exists"; return; fi
  say "Creating web app $app"
  if [ "$DOTNET_SELF_CONTAINED" = "true" ]; then
    # Self-contained: run as a generic Linux app; 03 ships the runtime in the zip.
    az webapp create -n "$app" -g "$RG" --plan "$PLAN_NAME" --runtime "DOTNETCORE:8.0" -o none
    warn "$app created on a placeholder stack — self-contained zip in 03 supplies .NET 10."
  else
    az webapp create -n "$app" -g "$RG" --plan "$PLAN_NAME" --runtime "$DOTNET_RUNTIME" -o none
  fi
  az webapp identity assign -n "$app" -g "$RG" -o none
  ok "Web app $app (managed identity on)"
}
create_webapp "$BPM_SVC_APP"
create_webapp "$ADMIN_SVC_APP"

# Grant each web app's identity read access to Key Vault secrets.
for app in "$BPM_SVC_APP" "$ADMIN_SVC_APP"; do
  pid="$(az webapp identity show -n "$app" -g "$RG" --query principalId -o tsv)"
  az role assignment create --assignee-object-id "$pid" --assignee-principal-type ServicePrincipal \
    --role "Key Vault Secrets User" --scope "$KV_ID" -o none 2>/dev/null || true
done
ok "Web app identities granted Key Vault read"

# ── Three Static Web Apps (frontends) ───────────────────────────────────────
create_swa() {
  local name="$1"
  if exists "az staticwebapp show -n '$name' -g '$RG'"; then ok "SWA $name exists"; return; fi
  say "Creating Static Web App $name"
  az staticwebapp create -n "$name" -g "$RG" -l "$LOCATION" -o none
  ok "SWA $name"
}
create_swa "$BPM_UI_SWA"
create_swa "$ADMIN_UI_SWA"
create_swa "$WWW_SWA"

# ── Optional: Azure Front Door for admin-ui same-origin routing ─────────────
if [ "$ENABLE_FRONT_DOOR" = "true" ]; then
  warn "ENABLE_FRONT_DOOR=true: Front Door routing for admin-ui (/api + /bpmsvc) is environment-specific."
  warn "01 provisions the profile + endpoint; review README §admin-ui for the origin/route wiring."
  if ! exists "az afd profile show --profile-name '$FD_PROFILE' -g '$RG'"; then
    az afd profile create --profile-name "$FD_PROFILE" -g "$RG" --sku Standard_AzureFrontDoor -o none
    ok "Front Door profile $FD_PROFILE"
  else ok "Front Door profile $FD_PROFILE exists"; fi
fi

echo
ok "Provisioning complete."
cat <<EOF

Resource summary (RG $RG):
  Postgres host : $PG_HOST   (db: $PG_DB, user: $PG_ADMIN_USER)
  bpm-svc       : https://$(az webapp show -n "$BPM_SVC_APP" -g "$RG" --query defaultHostName -o tsv)
  admin-svc     : https://$(az webapp show -n "$ADMIN_SVC_APP" -g "$RG" --query defaultHostName -o tsv)
  bpm-ui  (SWA) : https://$(az staticwebapp show -n "$BPM_UI_SWA" -g "$RG" --query defaultHostname -o tsv)
  admin-ui(SWA) : https://$(az staticwebapp show -n "$ADMIN_UI_SWA" -g "$RG" --query defaultHostname -o tsv)
  www     (SWA) : https://$(az staticwebapp show -n "$WWW_SWA" -g "$RG" --query defaultHostname -o tsv)
  Key Vault     : $KV_NAME

Next: ./02-configure.sh   (app settings, Key Vault refs, CORS, migrations)
Then add the GoDaddy CNAME/TXT records (README §DNS) for the custom domains.
EOF
