#!/usr/bin/env bash
set -e

cd "$(dirname "$0")"

BE_DIR="bpm-svc/src/Api"
FE_DIR="bpm-ui"
LOG_DIR=".launch-logs"
mkdir -p "$LOG_DIR"

BE_LOG="$LOG_DIR/be.log"
FE_LOG="$LOG_DIR/fe.log"

cleanup() {
    echo
    echo "stopping..."
    [[ -n "${BE_PID:-}" ]] && kill "$BE_PID" 2>/dev/null || true
    [[ -n "${FE_PID:-}" ]] && kill "$FE_PID" 2>/dev/null || true
    wait 2>/dev/null || true
    exit 0
}
trap cleanup INT TERM

echo "starting BE (dotnet run in $BE_DIR) -> $BE_LOG"
( cd "$BE_DIR" && dotnet run ) >"$BE_LOG" 2>&1 &
BE_PID=$!

echo "starting FE (npm run dev in $FE_DIR) -> $FE_LOG"
( cd "$FE_DIR" && npm run dev ) >"$FE_LOG" 2>&1 &
FE_PID=$!

echo
echo "BE pid=$BE_PID  FE pid=$FE_PID"
echo "tail -f $BE_LOG  |  tail -f $FE_LOG"
echo "Ctrl+C to stop both"
echo

wait
