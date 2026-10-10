#!/usr/bin/env bash
# Stream a pg_dump of the env's Postgres straight into Azure Blob (no local file).
# Produces <stamp>/flowcook.dump (pg_dump -Fc) + <stamp>/manifest.json in the
# backup storage account. Restore steps: BACKUP-RESTORE.md.
#
# Prereqs: az login; pg_dump 16 on PATH; python3 with azure-storage-blob
#   (PYTHON=/path/to/venv/bin/python3 to pick a venv). Your IP must be allowed
#   on the server firewall (script adds+removes a temp rule).
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=/dev/null
source "$HERE/${CONFIG:-00-config.sh}"

BACKUP_RG="${BACKUP_RG:-rg-flowcook-backup}"
BACKUP_SA="${BACKUP_SA:-stflowcookbackup}"
BACKUP_CONTAINER="${BACKUP_CONTAINER:-pg-dumps}"
PYTHON="${PYTHON:-python3}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
PREFIX="${ENV_PREFIX}/${STAMP}"

az account show >/dev/null 2>&1 || die "Not logged in — run 'az login'."
command -v pg_dump >/dev/null || die "pg_dump not found (brew install postgresql@16)"

say "Resolving server + credentials"
PG_HOST="$(az postgres flexible-server show -n "$PG_NAME" -g "$RG" --query fullyQualifiedDomainName -o tsv)"
PG_STATE="$(az postgres flexible-server show -n "$PG_NAME" -g "$RG" --query state -o tsv)"
[ "$PG_STATE" = "Ready" ] || die "Server state is $PG_STATE — start it first (./flowcook-start.sh)"
export PGPASSWORD; PGPASSWORD="$(az keyvault secret show --vault-name "$KV_NAME" -n pg-admin-password --query value -o tsv)"
export AZURE_STORAGE_KEY; AZURE_STORAGE_KEY="$(az storage account keys list -n "$BACKUP_SA" -g "$BACKUP_RG" --query '[0].value' -o tsv)"

MYIP="$(curl -s --max-time 10 https://api.ipify.org)"
RULE="tmp-backup-$STAMP"
say "Opening firewall for $MYIP ($RULE)"
az postgres flexible-server firewall-rule create -n "$PG_NAME" -g "$RG" --rule-name "$RULE" \
  --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
cleanup() { az postgres flexible-server firewall-rule delete -n "$PG_NAME" -g "$RG" --rule-name "$RULE" --yes -o none 2>/dev/null || true; }
trap cleanup EXIT

CONN="host=$PG_HOST port=5432 dbname=$PG_DB user=$PG_ADMIN_USER sslmode=require"
PG_VER="$(psql "$CONN" -At -c 'show server_version')"
DB_BYTES="$(psql "$CONN" -At -c "select pg_database_size('$PG_DB')")"

say "Streaming pg_dump -Fc $PG_DB → $BACKUP_SA/$BACKUP_CONTAINER/$PREFIX/flowcook.dump"
RESULT="$(pg_dump "$CONN" -Fc --no-owner --no-privileges \
  | "$PYTHON" "$HERE/pg_stream_upload.py" "$BACKUP_SA" "$BACKUP_CONTAINER" "$PREFIX/flowcook.dump")"
echo "$RESULT"

say "Writing manifest"
"$PYTHON" - "$RESULT" <<PY | "$PYTHON" "$HERE/pg_stream_upload.py" "$BACKUP_SA" "$BACKUP_CONTAINER" "$PREFIX/manifest.json" >/dev/null
import json, sys
r = json.loads(sys.argv[1])
print(json.dumps({
  "env": "$ENV_PREFIX", "taken_utc": "$STAMP",
  "source": {"server": "$PG_NAME", "host": "$PG_HOST", "database": "$PG_DB",
             "admin_user": "$PG_ADMIN_USER", "server_version": "$PG_VER",
             "sku": "$PG_SKU", "db_size_bytes": int("$DB_BYTES")},
  "dump": {"format": "pg_dump -Fc --no-owner --no-privileges", "pg_dump_version": "$(pg_dump --version)",
           "bytes": r["bytes"], "sha256": r["sha256"], "url": r["url"]},
  "secrets": {"pg_admin_password": "Key Vault $KV_NAME / pg-admin-password"},
  "restore": "infra/azure/BACKUP-RESTORE.md"
}, indent=2))
PY
ok "Backup done: $PREFIX  (sha256 $(echo "$RESULT" | "$PYTHON" -c 'import json,sys;print(json.load(sys.stdin)["sha256"][:16])')…)"
