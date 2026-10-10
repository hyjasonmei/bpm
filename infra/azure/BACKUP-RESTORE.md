# flowcook Postgres — backup to Blob & restore

Why this exists: the POC Postgres Flexible Server (`pg-poc-flowcook`, B1ms) costs
~NT$750/month even when nothing uses it, and a *stopped* Flexible Server
auto-restarts after 7 days (Azure limit), so `flowcook-stop.sh` only buys a week.
When the env is idle for longer, **dump → Blob → delete the server**, and
recreate from the dump when it's needed again. Blob storage for the dump is
effectively free (dump ≈ 300 KB on Cool tier ≈ NT$0.00x/month).

Nothing touches local disk: `pg_dump` is piped straight into a block blob, and
restore pipes the blob straight into `pg_restore`.

## Where the backups live

| Item | Value |
|---|---|
| Resource group | `rg-flowcook-backup` (japaneast) — **separate** from `rg-poc` so tearing down the env can't take the backups with it; has a `CanNotDelete` lock |
| Storage account | `stflowcookbackup` — StorageV2, Standard_LRS, Cool, no public access, TLS 1.2, blob + container soft-delete 30 days |
| Container | `pg-dumps` |
| Layout | `<env>/<UTC stamp>/flowcook.dump` (`pg_dump -Fc --no-owner --no-privileges`) + `<env>/<stamp>/manifest.json` (source server, version, size, **sha256**) |
| DB admin password | Key Vault `kv-poc-flowcook` / `pg-admin-password` — **keep the Key Vault when deleting the server**; restore reuses this password so `bpm-connection-string` stays valid |

List what's there:

```bash
az storage blob list -c pg-dumps --account-name stflowcookbackup --auth-mode key \
  --query "[].{name:name,bytes:properties.contentLength,tier:properties.blobTier}" -o table
```

## Prerequisites (one-off per machine)

```bash
brew install postgresql@16            # pg_dump / pg_restore 16 (server is PG 16)
python3 -m venv ~/.venvs/azblob && ~/.venvs/azblob/bin/pip install azure-storage-blob azure-identity
export PYTHON=~/.venvs/azblob/bin/python3
az login && az account set -s TODOSLN
```

## Backup (server running)

```bash
cd infra/azure
PYTHON=~/.venvs/azblob/bin/python3 ./flowcook-pg-backup.sh
```

What it does: resolves host + password (Key Vault) + storage key, opens a
temporary firewall rule for your IP (removed on exit), streams
`pg_dump -Fc` into `pg-dumps/<env>/<stamp>/flowcook.dump`, then writes
`manifest.json` with the sha256. Takes ~30 s for the POC db.

Verify a dump without restoring (lists the TOC; 60 `TABLE DATA` entries for POC):

```bash
AZURE_STORAGE_KEY=$(az storage account keys list -n stflowcookbackup -g rg-flowcook-backup --query '[0].value' -o tsv) \
$PYTHON pg_stream_download.py stflowcookbackup pg-dumps poc/<stamp>/flowcook.dump | pg_restore -l | grep -c 'TABLE DATA'
```

## Delete the server (after a verified backup)

```bash
source 00-config.sh
az postgres flexible-server delete -n "$PG_NAME" -g "$RG" --yes
```

Only the server goes. Keep: Key Vault (password + connection string), the App
Services (Stopped, F1 = NT$0), the SWAs (Free). `flowcook-start.sh` will fail
until the server is recreated — expected.

## Restore (full procedure, after the server was deleted)

Order matters: **the apps run EF migrations + seed on boot**. If they start
against an empty database they will create a fresh schema and the restore will
then refuse (db not empty). Restore *before* starting any app.

```bash
cd infra/azure
export PYTHON=~/.venvs/azblob/bin/python3

# 1. Recreate the server with the SAME admin password (so Key Vault refs stay valid).
#    01-provision.sh is idempotent — everything else already exists and is skipped.
#    ~5–10 min. Creates the empty 'flowcook' database.
export PG_ADMIN_PASSWORD="$(az keyvault secret show --vault-name kv-poc-flowcook -n pg-admin-password --query value -o tsv)"
./01-provision.sh

# 2. Stream the latest dump (or a specific stamp) into the empty db.
#    Refuses if the db already has tables. Verifies sha256 against the manifest.
./flowcook-pg-restore.sh            # or: ./flowcook-pg-restore.sh 20261010T125828Z

# 3. (Only if the hostname changed — it won't when the name is reused) refresh the
#    connection string in Key Vault:
./02-configure.sh

# 4. Start the apps.
./flowcook-start.sh
```

Smoke: `curl -sf https://<bpm-svc host>/health && echo OK`, then log in to the
admin UI with a seeded account — existing flows/cases should be there.

### Restoring somewhere else (local Postgres, another env)

Same stream, different target:

```bash
AZURE_STORAGE_KEY=$(az storage account keys list -n stflowcookbackup -g rg-flowcook-backup --query '[0].value' -o tsv)
createdb flowcook_from_backup
$PYTHON pg_stream_download.py stflowcookbackup pg-dumps poc/<stamp>/flowcook.dump \
  | pg_restore -d "host=localhost dbname=flowcook_from_backup" --no-owner --no-privileges
```

For another Azure env (`CONFIG=00-config.<env>.sh`), the restore script looks
under that env's own prefix. To seed a new env from the POC dump, copy the blob
to `<newenv>/<stamp>/` first (`az storage blob copy start`), or use the manual
pipe above against the new server.

## Verified

2026-10-10: `poc/20261010T125828Z` (297,225 bytes, sha256 `e9f1a91e…c9f004`)
streamed back into a local PG 16 and compared table-by-table with the live
server: 60 tables, 539 rows, identical counts. The Azure-side recreate path
(step 1 of Restore) reuses `01-provision.sh`, which is the script that built
the current server; it has not been re-run against a deleted server yet — budget
15 min the first time and check `az postgres flexible-server show` state is
`Ready` before step 2.

## Files

```
flowcook-pg-backup.sh    pg_dump → blob (streamed) + manifest
flowcook-pg-restore.sh   blob → pg_restore (streamed), sha256-checked, refuses non-empty db
pg_stream_upload.py      stdin → block blob (hashes while uploading)
pg_stream_download.py    blob → stdout
```
