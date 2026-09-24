#!/bin/bash
# Pull steady-state [Perf] windows: launch the build, let it run WAIT seconds, pull its log.
#   tools/cast_shadows/perf_pull.sh <none|all> [wait_s]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; cd "$ROOT"
v="$1"; WAIT="${2:-30}"
DEV="${DEVICE_ID:-5DB969FF-269C-5A8A-86EB-99EC9FF22397}"
B="com.rafehatfield.underwarden.perf$v"
EV=tools/cast_shadows/evidence
xcrun devicectl device process launch --device "$DEV" --terminate-existing "$B" > /dev/null 2>&1
sleep "$WAIT"
xcrun devicectl device copy from --device "$DEV" --domain-type appDataContainer --domain-identifier "$B" \
  --source Documents/diag.log --destination "$EV/perf_${v}_boot.log" > /dev/null 2>&1
echo "== perf$v, after ${WAIT}s:"
grep "\[Perf\]" "$EV/perf_${v}_boot.log" | sed -E 's/.*\[Perf\] //; s/ \(radius.*//' | sed 's/^/   /'
