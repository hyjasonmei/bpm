#!/usr/bin/env python3
"""Stream stdin into an Azure Block Blob without touching local disk.

Usage: <producer> | python3 pg_stream_upload.py <account> <container> <blob>
Auth:  AZURE_STORAGE_KEY env (account key) or, if absent, Azure CLI login.
Prints JSON {bytes, sha256, url} on success.
Requires: pip install azure-storage-blob azure-identity
"""
import hashlib, json, os, sys
from azure.storage.blob import BlobClient, StandardBlobTier

account, container, blob = sys.argv[1:4]
url = f"https://{account}.blob.core.windows.net/{container}/{blob}"
key = os.environ.get("AZURE_STORAGE_KEY")
if key:
    client = BlobClient.from_blob_url(url, credential=key)
else:
    from azure.identity import AzureCliCredential
    client = BlobClient.from_blob_url(url, credential=AzureCliCredential())

class Tee:
    """Wrap stdin so we can hash while the SDK reads."""
    def __init__(self, f):
        self.f, self.h, self.n = f, hashlib.sha256(), 0
    def read(self, size=-1):
        b = self.f.read(size)
        self.h.update(b); self.n += len(b)
        return b

tee = Tee(sys.stdin.buffer)
client.upload_blob(tee, blob_type="BlockBlob", overwrite=False,
                   max_concurrency=1, standard_blob_tier=StandardBlobTier.COOL)
if tee.n == 0:
    client.delete_blob(); print("ERROR: empty input, blob removed", file=sys.stderr); sys.exit(1)
print(json.dumps({"bytes": tee.n, "sha256": tee.h.hexdigest(), "url": url}))
