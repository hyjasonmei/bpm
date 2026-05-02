#!/usr/bin/env bash
set -e

cd "$(dirname "$0")"

DB="bpm-svc/src/Api/bpm.db"
if [[ -f "$DB" ]]; then
    rm "$DB"
    echo "removed $DB"
else
    echo "$DB not found, skipping"
fi

exec claude --dangerously-skip-permissions
