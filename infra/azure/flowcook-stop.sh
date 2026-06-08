#!/usr/bin/env bash
# Stop the compute to save money when not demoing: both App Services + Postgres.
# Storage (~$3/mo) keeps charging; compute (~$14/mo) stops. Frontends are static
# SWAs (Free tier) — nothing to stop. Run ./flowcook-start.sh to bring it back.
#
# NB: a stopped Postgres Flexible Server auto-restarts after 7 days (Azure limit).
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"
az account show >/dev/null 2>&1 || die "Not logged in — run 'az login'."

say "Stopping compute for env '$ENV_PREFIX' (RG $RG)…"
az webapp stop -n "$BPM_SVC_APP" -g "$RG" -o none && ok "stopped $BPM_SVC_APP"
az webapp stop -n "$ADMIN_SVC_APP" -g "$RG" -o none && ok "stopped $ADMIN_SVC_APP"
az postgres flexible-server stop -n "$PG_NAME" -g "$RG" -o none 2>&1 | tail -1 && ok "stopping $PG_NAME (takes a minute)"
ok "Compute stopped. Only storage (~\$3/mo) still bills. ./flowcook-start.sh to resume."
