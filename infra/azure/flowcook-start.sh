#!/usr/bin/env bash
# Bring the env back up after ./flowcook-stop.sh: Postgres first (the APIs need
# it to migrate/connect on boot), then both App Services. Cold start + EF migrate
# means the APIs take 1-2 min to answer after this returns.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"
az account show >/dev/null 2>&1 || die "Not logged in — run 'az login'."

say "Starting env '$ENV_PREFIX' (RG $RG)…"
# Postgres first — and wait for it, since the APIs fail to boot without the db.
az postgres flexible-server start -n "$PG_NAME" -g "$RG" -o none 2>&1 | tail -1 && ok "started $PG_NAME"
az webapp start -n "$ADMIN_SVC_APP" -g "$RG" -o none && ok "started $ADMIN_SVC_APP"
az webapp start -n "$BPM_SVC_APP" -g "$RG" -o none && ok "started $BPM_SVC_APP"
echo
ok "Started. APIs need ~1-2 min to cold-start + connect. Health check:"
echo "  curl -sf https://$(az webapp show -n "$BPM_SVC_APP" -g "$RG" --query defaultHostName -o tsv)/health && echo OK"
