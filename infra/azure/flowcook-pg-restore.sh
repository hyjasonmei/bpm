#!/usr/bin/env bash
# Restore a flowcook Postgres dump from Azure Blob into the env's server — streamed,
# no local file. Counterpart of flowcook-pg-backup.sh. Full procedure (incl. how to
# recreate the server after a teardown): BACKUP-RESTORE.md.
#
# Usage: ./flowcook-pg-restore.sh [<stamp>|latest]     (default: latest)
# Prereqs: az login; pg_restore 16; python3 with azure-storage-blob
#   (PYTHON=/path/to/venv/bin/python3). Target server must exist and be Ready;
#   target database must be EMPTY (fresh from 01-provision.sh) — the script refuses
#   otherwise, so you never restore over live data by accident.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"

BACKUP_RG="${BACKUP_RG:-rg-flowcook-backup}"
BACKUP_SA="${BACKUP_SA:-stflowcookbackup}"
BACKUP_CONTAINER="${BACKUP_CONTAINER:-pg-dumps}"
PYTHON="${PYTHON:-python3}"
WANT="${1:-latest}"

az account show >/dev/null 2>&1 || die "Not logged in — run 'az login'."
command -v pg_restore >/dev/null || die "pg_restore not found (brew install postgresql@16)"
export AZURE_STORAGE_KEY; AZURE_STORAGE_KEY="$(az storage account keys list -n "$BACKUP_SA" -g "$BACKUP_RG" --query '[0].value' -o tsv)"

# ── Pick the dump ────────────────────────────────────────────────────────────
if [ "$WANT" = "latest" ]; then
  STAMP="$(az storage blob list -c "$BACKUP_CONTAINER" --account-name "$BACKUP_SA" --account-key "$AZURE_STORAGE_KEY" \
    --prefix "$ENV_PREFIX/" --query "[?ends_with(name,'/flowcook.dump')].name" -o tsv | sed -E 's#^[^/]+/([^/]+)/.*#\1#' | sort | tail -1)"
  [ -n "$STAMP" ] || die "No dumps found under $BACKUP_SA/$BACKUP_CONTAINER/$ENV_PREFIX/"
else
  STAMP="$WANT"
fi
BLOB="$ENV_PREFIX/$STAMP/flowcook.dump"
MANIFEST="$("$PYTHON" "$HERE/pg_stream_download.py" "$BACKUP_SA" "$BACKUP_CONTAINER" "$ENV_PREFIX/$STAMP/manifest.json")"
WANT_SHA="$(echo "$MANIFEST" | "$PYTHON" -c 'import json,sys;print(json.load(sys.stdin)["dump"]["sha256"])')"
say "Restoring $BLOB  (taken $STAMP, sha256 ${WANT_SHA:0:16}…)"

# ── Target server ────────────────────────────────────────────────────────────
PG_HOST="$(az postgres flexible-server show -n "$PG_NAME" -g "$RG" --query fullyQualifiedDomainName -o tsv)" \
  || die "Server $PG_NAME not found — run 01-provision.sh first (see BACKUP-RESTORE.md)"
[ "$(az postgres flexible-server show -n "$PG_NAME" -g "$RG" --query state -o tsv)" = "Ready" ] || die "Server not Ready"
export PGPASSWORD; PGPASSWORD="$(az keyvault secret show --vault-name "$KV_NAME" -n pg-admin-password --query value -o tsv)"

MYIP="$(curl -s --max-time 10 https://api.ipify.org)"
RULE="tmp-restore-$(date -u +%Y%m%dT%H%M%SZ)"
say "Opening firewall for $MYIP ($RULE)"
az postgres flexible-server firewall-rule create -n "$PG_NAME" -g "$RG" --rule-name "$RULE" \
  --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
cleanup() { az postgres flexible-server firewall-rule delete -n "$PG_NAME" -g "$RG" --rule-name "$RULE" --yes -o none 2>/dev/null || true; }
trap cleanup EXIT
sleep 15   # firewall propagation

CONN="host=$PG_HOST port=5432 dbname=$PG_DB user=$PG_ADMIN_USER sslmode=require"
NTABLES="$(psql "$CONN" -At -c "select count(*) from information_schema.tables where table_schema='public'")"
[ "$NTABLES" = "0" ] || die "Target db '$PG_DB' already has $NTABLES tables — refusing. Drop/recreate it first if you really mean it."

# ── Stream: blob → sha check (tee) → pg_restore ──────────────────────────────
say "Streaming pg_restore into $PG_HOST/$PG_DB"
SHA_FILE="$(mktemp)"
"$PYTHON" "$HERE/pg_stream_download.py" "$BACKUP_SA" "$BACKUP_CONTAINER" "$BLOB" \
  | tee >(shasum -a 256 | awk '{print $1}' > "$SHA_FILE") \
  | pg_restore -d "$CONN" --no-owner --no-privileges --exit-on-error
GOT_SHA="$(cat "$SHA_FILE")"; rm -f "$SHA_FILE"
[ "$GOT_SHA" = "$WANT_SHA" ] || die "sha256 mismatch after restore (got $GOT_SHA) — blob corrupted? investigate before starting apps"

RESTORED="$(psql "$CONN" -At -c "select count(*) from information_schema.tables where table_schema='public' and table_type='BASE TABLE'")"
ok "Restored $RESTORED tables from $STAMP; sha256 verified."
echo "Next: ./flowcook-start.sh (apps boot, EF sees migrations already applied, no re-seed)."
