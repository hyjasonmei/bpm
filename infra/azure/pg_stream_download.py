#!/usr/bin/env python3
"""Stream an Azure blob to stdout (no local file). Pair with pg_restore.

Usage: python3 pg_stream_download.py <account> <container> <blob> | pg_restore ...
Auth:  AZURE_STORAGE_KEY env (account key) or, if absent, Azure CLI login.
"""
import os, signal, sys
signal.signal(signal.SIGPIPE, signal.SIG_DFL)  # quiet exit when the consumer (e.g. pg_restore -l) closes early
from azure.storage.blob import BlobClient

account, container, blob = sys.argv[1:4]
url = f"https://{account}.blob.core.windows.net/{container}/{blob}"
key = os.environ.get("AZURE_STORAGE_KEY")
if key:
    client = BlobClient.from_blob_url(url, credential=key)
else:
    from azure.identity import AzureCliCredential
    client = BlobClient.from_blob_url(url, credential=AzureCliCredential())
client.download_blob(max_concurrency=1).readinto(sys.stdout.buffer)
