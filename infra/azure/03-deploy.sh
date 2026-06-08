#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# 03-deploy.sh — build + publish code to the provisioned resources.
# Order matters: admin-svc first (creates Admin_* tables + self-seeds identity on
# startup), then bpm-svc (feature/bpm tables), then the three frontends.
#
# APIs  : dotnet publish → zip → `az webapp deploy`. Framework-dependent against
#         the App Service .NET stack, or self-contained linux-x64 if
#         DOTNET_SELF_CONTAINED=true (00-config).
# Front : npm ci && build (with the right VITE_* baked in) → `swa deploy`.
#
# Prereqs: dotnet 10 SDK, node/npm, and the SWA CLI (`npm i -g @azure/static-web-apps-cli`).
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"
az account show >/dev/null 2>&1 || die "Not logged in — run 'az login'."
command -v dotnet >/dev/null || die "dotnet SDK not found."
command -v npm >/dev/null || die "npm not found."

resolve_azure_hostnames   # option A: bake Azure default hostnames into VITE_* URLs

WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT

# ── Deploy one .NET API ─────────────────────────────────────────────────────
deploy_api() {
  local app="$1" proj="$2" assembly="$3"
  local out="$WORK/$app"
  say "Publishing $app ($(basename "$proj"))"
  if [ "$DOTNET_SELF_CONTAINED" = "true" ]; then
    dotnet publish "$proj" -c Release -r linux-x64 --self-contained true -o "$out" -p:PublishTrimmed=false
    az webapp config set -n "$app" -g "$RG" --startup-file "$assembly" -o none
  else
    dotnet publish "$proj" -c Release -o "$out"
    az webapp config set -n "$app" -g "$RG" --startup-file "dotnet $assembly.dll" -o none
  fi
  ( cd "$out" && zip -qr "$WORK/$app.zip" . )
  say "Deploying $app → Azure"
  # --track-status false: don't block on the startup probe. A cold start that
  # also runs EF migrate + first-boot seed can exceed the 10-min deploy timeout
  # and report a false "Site failed to start" even though the app comes up fine.
  az webapp deploy -n "$app" -g "$RG" --src-path "$WORK/$app.zip" --type zip --track-status false -o none
  ok "$app deployed → https://$(az webapp show -n "$app" -g "$RG" --query defaultHostName -o tsv)"
}

# admin-svc FIRST (migrations + identity seed run on its startup).
deploy_api "$ADMIN_SVC_APP" "$ADMIN_SVC_PROJ" "Bpm.Admin.Api"
say "Waiting 20s for admin-svc to migrate + seed before bringing up bpm-svc…"
sleep 20
deploy_api "$BPM_SVC_APP" "$BPM_SVC_PROJ" "Bpm.Api"   # csproj AssemblyName is Bpm.Api, not Api

# ── Deploy one Static Web App ───────────────────────────────────────────────
command -v swa >/dev/null || die "SWA CLI not found — npm i -g @azure/static-web-apps-cli"
deploy_swa() {
  local name="$1" dir="$2" dist="$3"
  local token; token="$(az staticwebapp secrets list -n "$name" -g "$RG" --query properties.apiKey -o tsv)"
  say "Building $name ($(basename "$dir"))"
  ( cd "$dir" && npm ci && npm run build )
  say "Deploying $name → Static Web App"
  swa deploy "$dir/$dist" --deployment-token "$token" --env production
  ok "$name deployed → https://$(az staticwebapp show -n "$name" -g "$RG" --query defaultHostname -o tsv)"
}

# bpm-ui needs the bpm-svc URL baked in at build time.
export VITE_BPM_SVC_URL="https://${BPM_API_FQDN}"
deploy_swa "$BPM_UI_SWA" "$BPM_UI_DIR" "dist"

# admin-ui (post unify-jwt): reaches admin-svc + bpm-svc cross-origin with a JWT
# bearer, so both base URLs are baked in at build time — no same-origin reverse
# proxy, no Front Door routing needed. admin-ui is a plain static SWA like the others.
export VITE_ADMIN_SVC_URL="https://${ADMIN_API_FQDN}"
# VITE_BPM_SVC_URL already exported above (admin-ui's /bpmsvc calls go to bpm-svc).
deploy_swa "$ADMIN_UI_SWA" "$ADMIN_UI_DIR" "dist"

deploy_swa "$WWW_SWA" "$WWW_DIR" "dist"

echo
ok "Deploy complete."
cat <<EOF

Smoke test:
  curl -sf https://$(az webapp show -n "$BPM_SVC_APP" -g "$RG" --query defaultHostName -o tsv)/health && echo OK
  curl -s -X POST https://$(az webapp show -n "$ADMIN_SVC_APP" -g "$RG" --query defaultHostName -o tsv)/api/auth/login \\
    -H 'Content-Type: application/json' -d '{"username":"alice@acme.example","password":"flowcook2026"}'

Remaining manual steps (README): GoDaddy DNS · custom-domain binding + managed TLS ·
SMTP/email domain auth · register+publish cooked flows in AI Kitchen.
(admin-ui same-origin proxy is GONE — unify-jwt made admin-ui a plain cross-origin SWA.)
EOF
